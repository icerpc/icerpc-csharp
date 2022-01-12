// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features.Internal;
using IceRpc.Slice;
using IceRpc.Slice.Internal;
using IceRpc.Transports;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Internal
{
    internal sealed class IceRpcProtocolConnection : IProtocolConnection
    {
        /// <inheritdoc/>
        public bool HasDispatchesInProgress
        {
            get
            {
                lock (_mutex)
                {
                    return _dispatches.Count > 0;
                }
            }
        }

        /// <inheritdoc/>
        public bool HasInvocationsInProgress
        {
            get
            {
                lock (_mutex)
                {
                    return _invocations.Count > 0;
                }
            }
        }

        /// <inheritdoc/>
        public event Action<string>? PeerShutdownInitiated;

        private IMultiplexedStream? _controlStream;
        private readonly HashSet<IncomingRequest> _dispatches = new();
        private readonly TaskCompletionSource _dispatchesAndInvocationsCompleted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly int _incomingFrameMaxSize;
        private readonly HashSet<IMultiplexedStream> _invocations = new();
        private long _lastRemoteBidirectionalStreamId = -1;
        // TODO: to we really need to keep track of this since we don't keep track of one-way requests?
        private long _lastRemoteUnidirectionalStreamId = -1;
        private readonly object _mutex = new();
        private readonly IMultiplexedNetworkConnection _networkConnection;
        private int? _peerIncomingFrameMaxSize;
        private CancellationToken _receiveRequestCancellationToken;
        private IMultiplexedStream? _remoteControlStream;
        private bool _shutdown;
        private readonly CancellationTokenSource _waitForShutdownCancellationSource = new();

        public void Dispose() => _waitForShutdownCancellationSource.Dispose();

        public Task PingAsync(CancellationToken cancel) => SendControlFrameAsync(IceRpcControlFrameType.Ping, null, cancel);

        /// <inheritdoc/>
        public async Task<IncomingRequest> ReceiveRequestAsync()
        {
            CancellationToken cancel = _receiveRequestCancellationToken;

            while (true)
            {
                IMultiplexedStream stream;
                PipeReader reader;

                try
                {
                    // Accepts a new stream.
                    stream = await _networkConnection.AcceptStreamAsync(cancel).ConfigureAwait(false);

                    // Receives the request frame from the stream.

                    // We cancel reading when we shutdown the connection.
                    reader = stream.Input;
                }
                catch
                {
                    lock (_mutex)
                    {
                        if (_shutdown && _invocations.Count == 0 && _dispatches.Count == 0)
                        {
                            // The connection was gracefully shut down, raise ConnectionClosedException here to ensure
                            // that the ClosedEvent will report this exception instead of the transport failure.
                            throw new ConnectionClosedException("connection gracefully shut down");
                        }
                    }
                    throw;
                }

                IceRpcRequestHeader header;
                IReadOnlyDictionary<int, ReadOnlyMemory<byte>> fields;
                FeatureCollection features = FeatureCollection.Empty;

                try
                {
                    ReadResult readResult = await reader.ReadSegmentAsync(Encoding.Slice20, cancel).ConfigureAwait(false);

                    // At this point, nothing can call CancelPendingReads on this pipe reader.
                    Debug.Assert(!readResult.IsCanceled);

                    if (readResult.Buffer.IsEmpty)
                    {
                        throw new InvalidDataException($"received icerpc request with empty header");
                    }

                    (header, fields) = DecodeHeader(readResult.Buffer);
                    reader.AdvanceTo(readResult.Buffer.End);

                    if (readResult.IsCompleted)
                    {
                        await reader.CompleteAsync().ConfigureAwait(false);
                        reader = PipeReader.Create(ReadOnlySequence<byte>.Empty);
                    }

                    if (header.Deadline < -1 || header.Deadline == 0)
                    {
                        throw new InvalidDataException($"received invalid deadline value {header.Deadline}");
                    }

                    // Decode Context from Fields and set corresponding feature.
                    if (fields.Get(
                        (int)FieldKey.Context,
                        (ref IceDecoder decoder) => decoder.DecodeDictionary(
                            minKeySize: 1,
                            minValueSize: 1,
                            keyDecodeFunc: (ref IceDecoder decoder) => decoder.DecodeString(),
                            valueDecodeFunc: (ref IceDecoder decoder) => decoder.DecodeString()))
                                is Dictionary<string, string> context)
                    {
                        features = features.WithContext(context);
                    }

                    if (header.Operation.Length == 0)
                    {
                        throw new InvalidDataException("received request with empty operation name");
                    }
                }
                catch (Exception ex)
                {
                    await reader.CompleteAsync(ex).ConfigureAwait(false);
                    throw;
                }

                var request = new IncomingRequest(
                    Protocol.IceRpc,
                    path: header.Path,
                    fragment: header.Fragment,
                    operation: header.Operation,
                    payload: reader,
                    payloadEncoding: header.PayloadEncoding.Length > 0 ?
                        Encoding.FromString(header.PayloadEncoding) : IceRpcDefinitions.Encoding,
                    responseWriter: stream.IsBidirectional ?
                        new MultiplexedStreamPipeWriter(stream) : InvalidPipeWriter.Instance)
                {
                    IsIdempotent = header.Idempotent,
                    IsOneway = !stream.IsBidirectional,
                    Features = features,
                    // The infinite deadline is encoded as -1 and converted to DateTime.MaxValue
                    Deadline = header.Deadline == -1 ?
                        DateTime.MaxValue : DateTime.UnixEpoch + TimeSpan.FromMilliseconds(header.Deadline),
                    Fields = fields
                };

                lock (_mutex)
                {
                    // If shutdown, ignore the incoming request and continue receiving frames until the
                    // connection is closed.
                    if (!_shutdown)
                    {
                        _dispatches.Add(request);
                        request.CancelDispatchSource = new();

                        if (stream.IsBidirectional)
                        {
                            _lastRemoteBidirectionalStreamId = stream.Id;
                        }
                        else
                        {
                            _lastRemoteUnidirectionalStreamId = stream.Id;
                        }

                        stream.ShutdownAction = () =>
                        {
                            request.CancelDispatchSource.Cancel();
                            request.CancelDispatchSource.Dispose();

                            lock (_mutex)
                            {
                                _dispatches.Remove(request);

                                // If no more invocations or dispatches and shutting down, shutdown can complete.
                                if (_shutdown && _invocations.Count == 0 && _dispatches.Count == 0)
                                {
                                    _dispatchesAndInvocationsCompleted.SetResult();
                                }
                            }
                        };
                        return request;
                    }
                }

                static (IceRpcRequestHeader, IReadOnlyDictionary<int, ReadOnlyMemory<byte>>) DecodeHeader(
                    ReadOnlySequence<byte> buffer)
                {
                    var decoder = new IceDecoder(buffer, Encoding.Slice20);
                    return (new IceRpcRequestHeader(ref decoder), decoder.DecodeFieldDictionary());
                }
            }
        }

        /// <inheritdoc/>
        public async Task<IncomingResponse> ReceiveResponseAsync(OutgoingRequest request, CancellationToken cancel)
        {
            // This class sent this request and set ResponseReader on it.
            Debug.Assert(request.ResponseReader != null);
            Debug.Assert(!request.IsOneway);

            IceRpcResponseHeader header;
            IReadOnlyDictionary<int, ReadOnlyMemory<byte>> fields;
            FeatureCollection features = FeatureCollection.Empty;

            PipeReader responseReader = request.ResponseReader;

            try
            {
                ReadResult readResult = await responseReader.ReadSegmentAsync(
                    Encoding.Slice20,
                    cancel).ConfigureAwait(false);

                // At this point, nothing can call CancelPendingReads on this pipe reader.
                Debug.Assert(!readResult.IsCanceled);

                if (readResult.Buffer.IsEmpty)
                {
                    throw new InvalidDataException($"received icerpc response with empty header");
                }

                (header, fields) = DecodeHeader(readResult.Buffer);
                responseReader.AdvanceTo(readResult.Buffer.End);

                RetryPolicy? retryPolicy = fields.Get(
                    (int)FieldKey.RetryPolicy, (ref IceDecoder decoder) => new RetryPolicy(ref decoder));
                if (retryPolicy != null)
                {
                    features = features.With(retryPolicy);
                }

                if (readResult.IsCompleted)
                {
                    await responseReader.CompleteAsync().ConfigureAwait(false);
                    responseReader = PipeReader.Create(ReadOnlySequence<byte>.Empty);
                }
            }
            catch (Exception ex)
            {
                await responseReader.CompleteAsync(ex).ConfigureAwait(false);
                throw;
            }

            var response = new IncomingResponse(
                Protocol.IceRpc,
                header.ResultType,
                responseReader,
                payloadEncoding: header.PayloadEncoding.Length > 0 ?
                    Encoding.FromString(header.PayloadEncoding) : IceRpcDefinitions.Encoding)
            {
                Features = features,
                Fields = fields,
                ProxyInvoker = request.Proxy.Invoker,
            };

            return response;

            static (IceRpcResponseHeader, IReadOnlyDictionary<int, ReadOnlyMemory<byte>>) DecodeHeader(
                ReadOnlySequence<byte> buffer)
            {
                var decoder = new IceDecoder(buffer, Encoding.Slice20);
                return (new IceRpcResponseHeader(ref decoder), decoder.DecodeFieldDictionary());
            }
        }

        /// <inheritdoc/>
        public async Task SendRequestAsync(OutgoingRequest request, CancellationToken cancel)
        {
            IMultiplexedStream stream;
            try
            {
                // Create the stream.
                stream = _networkConnection.CreateStream(!request.IsOneway);
                request.InitialPayloadSink.SetDecoratee(stream.Output);

                // Keep track of the invocation for the shutdown logic.
                if (!request.IsOneway || request.PayloadSourceStream != null)
                {
                    lock (_mutex)
                    {
                        if (_shutdown)
                        {
                            stream.Abort(MultiplexedStreamError.ConnectionShutdown);
                            throw new ConnectionClosedException("connection shutdown");
                        }
                        _invocations.Add(stream);

                        stream.ShutdownAction = () =>
                        {
                            lock (_mutex)
                            {
                                _invocations.Remove(stream);

                                // If no more invocations or dispatches and shutting down, shutdown can complete.
                                if (_shutdown && _invocations.Count == 0 && _dispatches.Count == 0)
                                {
                                    _dispatchesAndInvocationsCompleted.SetResult();
                                }
                            }
                        };
                    }
                }

                EncodeHeader();

                await SendPayloadAsync(
                    request.PayloadSource,
                    request.PayloadSourceStream,
                    request.PayloadSink,
                    cancel).ConfigureAwait(false);

                request.IsSent = true;
            }
            catch (Exception ex)
            {
                await request.PayloadSource.CompleteAsync(ex).ConfigureAwait(false);
                if (request.PayloadSourceStream != null)
                {
                    await request.PayloadSourceStream.CompleteAsync(ex).ConfigureAwait(false);
                }
                await request.PayloadSink.CompleteAsync(ex).ConfigureAwait(false);
                throw;
            }

            if (!request.IsOneway)
            {
                request.ResponseReader = stream.Input;
            }

            void EncodeHeader()
            {
                var encoder = new IceEncoder(stream.Output, Encoding.Slice20);

                // Write the IceRpc request header.
                Memory<byte> sizePlaceholder = encoder.GetPlaceholderMemory(2);
                int headerStartPos = encoder.EncodedByteCount; // does not include the size

                // DateTime.MaxValue represents an infinite deadline and it is encoded as -1
                long deadline = request.Deadline == DateTime.MaxValue ? -1 :
                        (long)(request.Deadline - DateTime.UnixEpoch).TotalMilliseconds;

                var header = new IceRpcRequestHeader(
                    request.Path,
                    request.Fragment,
                    request.Operation,
                    request.IsIdempotent,
                    deadline,
                    request.PayloadEncoding == IceRpcDefinitions.Encoding ? "" : request.PayloadEncoding.ToString());

                header.Encode(ref encoder);

                // If the context feature is set to a non empty context, or if the fields defaults contains a context
                // entry and the context feature is set, marshal the context feature in the request fields. The context
                // feature must prevail over field defaults. Cannot use request.Features.GetContext it doesn't
                // distinguish between empty an non set context.
                if (request.Features.Get<Context>()?.Value is IDictionary<string, string> context &&
                    (context.Count > 0 || request.FieldsDefaults.ContainsKey((int)FieldKey.Context)))
                {
                    // Encodes context
                    request.Fields[(int)FieldKey.Context] =
                        (ref IceEncoder encoder) => encoder.EncodeDictionary(
                            context,
                            (ref IceEncoder encoder, string value) => encoder.EncodeString(value),
                            (ref IceEncoder encoder, string value) => encoder.EncodeString(value));
                }
                // else context remains empty (not set)

                encoder.EncodeFields(request.Fields, request.FieldsDefaults);

                // We're done with the header encoding, write the header size.
                Slice20Encoding.EncodeSize(encoder.EncodedByteCount - headerStartPos, sizePlaceholder.Span);
            }
        }

        /// <inheritdoc/>
        public async Task SendResponseAsync(
            OutgoingResponse response,
            IncomingRequest request,
            CancellationToken cancel)
        {
            if (request.IsOneway)
            {
                await response.PayloadSource.CompleteAsync().ConfigureAwait(false);
                await response.PayloadSink.CompleteAsync().ConfigureAwait(false);
                return;
            }

            EncodeHeader();

            await SendPayloadAsync(
                response.PayloadSource,
                response.PayloadSourceStream,
                response.PayloadSink,
                cancel).ConfigureAwait(false);

            void EncodeHeader()
            {
                var encoder = new IceEncoder(request.ResponseWriter, Encoding.Slice20);

                // Write the IceRpc response header.
                Memory<byte> sizePlaceholder = encoder.GetPlaceholderMemory(2);
                int headerStartPos = encoder.EncodedByteCount;

                new IceRpcResponseHeader(
                    response.ResultType,
                    response.PayloadEncoding == IceRpcDefinitions.Encoding ? "" :
                        response.PayloadEncoding.ToString()).Encode(ref encoder);

                encoder.EncodeFields(response.Fields, response.FieldsDefaults);

                // We're done with the header encoding, write the header size.
                Slice20Encoding.EncodeSize(encoder.EncodedByteCount - headerStartPos, sizePlaceholder.Span);
            }
        }

        /// <inheritdoc/>
        public async Task ShutdownAsync(string message, CancellationToken cancel)
        {
            bool alreadyShuttingDown = false;
            lock (_mutex)
            {
                if (_shutdown)
                {
                    alreadyShuttingDown = true;
                }
                else
                {
                    // Mark the connection as shutdown to prevent further request from being accepted.
                    _shutdown = true;
                    if (_invocations.Count == 0 && _dispatches.Count == 0)
                    {
                        _dispatchesAndInvocationsCompleted.SetResult();
                    }
                }
            }

            // Canceling WaitForShutdown will cancel dispatches and invocations to speed up shutdown.
            using CancellationTokenRegistration _ = cancel.Register(() =>
                {
                    try
                    {
                        _waitForShutdownCancellationSource.Cancel();
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                });

            if (!alreadyShuttingDown)
            {
                // Send GoAway frame
                await SendControlFrameAsync(
                    IceRpcControlFrameType.GoAway,
                    (ref IceEncoder encoder) => new IceRpcGoAwayBody(
                        _lastRemoteBidirectionalStreamId,
                        _lastRemoteUnidirectionalStreamId,
                        message).Encode(ref encoder),
                    CancellationToken.None).ConfigureAwait(false);
            }

            // Wait for the control streams to complete. The control streams are terminated once the peer closes the
            // the connection.
            await _controlStream!.WaitForShutdownAsync(CancellationToken.None).ConfigureAwait(false);
            await _remoteControlStream!.WaitForShutdownAsync(CancellationToken.None).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        internal IceRpcProtocolConnection(IMultiplexedNetworkConnection networkConnection, int incomingFrameMaxSize)
        {
            _networkConnection = networkConnection;
            _incomingFrameMaxSize = incomingFrameMaxSize;
        }

        internal async Task InitializeAsync(CancellationToken cancel)
        {
            // Create the control stream and send the protocol initialize frame
            _controlStream = _networkConnection.CreateStream(false);

            await SendControlFrameAsync(
                IceRpcControlFrameType.Initialize,
                (ref IceEncoder encoder) =>
                {
                    // Encode the transport parameters as Fields
                    encoder.EncodeSize(1);

                    encoder.EncodeVarInt((int)IceRpcParameterKey.IncomingFrameMaxSize);
                    Span<byte> sizePlaceholder = encoder.GetPlaceholderSpan(2);
                    int startPos = encoder.EncodedByteCount;
                    encoder.EncodeVarULong((ulong)_incomingFrameMaxSize);
                    Slice20Encoding.EncodeSize(encoder.EncodedByteCount - startPos, sizePlaceholder);
                },
                cancel).ConfigureAwait(false);

            // Wait for the remote control stream to be accepted and read the protocol initialize frame
            _remoteControlStream = await _networkConnection.AcceptStreamAsync(cancel).ConfigureAwait(false);

            ReadOnlyMemory<byte> buffer = await ReceiveControlFrameAsync(
                IceRpcControlFrameType.Initialize,
                cancel).ConfigureAwait(false);

            // Read the protocol parameters which are encoded as IceRpc.Fields.
            _peerIncomingFrameMaxSize = DecodePeerIncomingFrameMaxSize(buffer);

            _ = Task.Run(
                async () =>
                {
                    using var source = new CancellationTokenSource();
                    _receiveRequestCancellationToken = source.Token;
                    try
                    {
                        await WaitForShutdownAsync().ConfigureAwait(false);
                    }
                    finally
                    {
                        // Unblock ReceiveRequest. The connection will close the connection upon getting an exception
                        // from ReceiveRequest.
                        source.Cancel();
                    }
                },
                CancellationToken.None);

            static int DecodePeerIncomingFrameMaxSize(ReadOnlyMemory<byte> buffer)
            {
                var decoder = new IceDecoder(buffer, Encoding.Slice20);
                int dictionarySize = decoder.DecodeSize();

                for (int i = 0; i < dictionarySize; ++i)
                {
                    int key = decoder.DecodeVarInt();
                    if (key == (int)IceRpcParameterKey.IncomingFrameMaxSize)
                    {
                        decoder.SkipSize();

                        int peerIncomingFrameMaxSize = checked((int)decoder.DecodeVarUInt());

                        if (peerIncomingFrameMaxSize < 1024)
                        {
                            throw new InvalidDataException($@"the peer's IncomingFrameMaxSize ({
                                peerIncomingFrameMaxSize} bytes) value is inferior to 1KB");
                        }
                        return peerIncomingFrameMaxSize;
                    }
                    else
                    {
                        // Ignore unsupported parameters.
                        decoder.Skip(decoder.DecodeSize());
                    }
                }

                throw new InvalidDataException("missing IncomingFrameMaxSize IceRpc connection parameter");
            }
        }

        private async ValueTask<ReadOnlyMemory<byte>> ReceiveControlFrameAsync(
            IceRpcControlFrameType expectedFrameType,
            CancellationToken cancel)
        {
            byte[] bufferArray = new byte[256];
            while (true)
            {
                var buffer = new Memory<byte>(bufferArray);

                // Read the frame type and first byte of the size.
                await _remoteControlStream!.ReadUntilFullAsync(buffer[0..2], cancel).ConfigureAwait(false);
                var frameType = (IceRpcControlFrameType)buffer.Span[0];
                if (frameType > IceRpcControlFrameType.GoAwayCompleted)
                {
                    throw new InvalidDataException($"invalid IceRpc frame type {frameType}");
                }

                // Read the remainder of the size if needed.
                int sizeLength = Slice20Encoding.DecodeSizeLength(buffer.Span[1]);
                if (sizeLength > 1)
                {
                    await _remoteControlStream!.ReadUntilFullAsync(
                        buffer.Slice(2, sizeLength - 1), cancel).ConfigureAwait(false);
                }

                int frameSize = Slice20Encoding.DecodeSize(buffer[1..].AsReadOnlySpan()).Size;
                if (frameSize > _incomingFrameMaxSize)
                {
                    throw new InvalidDataException(
                        $"frame with {frameSize} bytes exceeds IncomingFrameMaxSize connection option value");
                }

                if (frameSize > 0)
                {
                    // TODO: rent buffer from Memory pool
                    buffer = frameSize > buffer.Length ? new byte[frameSize] : buffer[..frameSize];
                    await _remoteControlStream!.ReadUntilFullAsync(buffer, cancel).ConfigureAwait(false);
                }
                else
                {
                    buffer = Memory<byte>.Empty;
                }

                if (frameType == IceRpcControlFrameType.Ping)
                {
                    // expected, nothing to do
                }
                else if (frameType != expectedFrameType)
                {
                    throw new InvalidDataException($"received frame type {frameType} but expected {expectedFrameType}");
                }
                else
                {
                    return buffer;
                }
            }
        }

        private async Task SendControlFrameAsync(
            IceRpcControlFrameType frameType,
            EncodeAction? frameEncodeAction,
            CancellationToken cancel)
        {
            Memory<byte> buffer = new byte[1024]; // TODO: use pooled memory?
            var bufferWriter = new SingleBufferWriter(buffer);
            Encode(bufferWriter);
            buffer = bufferWriter.WrittenBuffer;

            await _controlStream!.Output.WriteAsync(buffer, cancel).ConfigureAwait(false);
            if (frameType == IceRpcControlFrameType.GoAwayCompleted)
            {
                // TODO: XXX: cancel token?
                await _controlStream!.Output.CompleteAsync().ConfigureAwait(false);
            }

            void Encode(IBufferWriter<byte> bufferWriter)
            {
                var encoder = new IceEncoder(bufferWriter, Encoding.Slice20);
                encoder.EncodeByte((byte)frameType);
                Memory<byte> sizePlaceholder = encoder.GetPlaceholderMemory(4); // TODO: reduce bytes
                int startPos = encoder.EncodedByteCount; // does not include the size
                frameEncodeAction?.Invoke(ref encoder);
                Slice20Encoding.EncodeSize(encoder.EncodedByteCount - startPos, sizePlaceholder.Span);
            }
        }

        /// <summary>Sends the payload source and payload source stream of an outgoing frame.</summary>
        private static async ValueTask SendPayloadAsync(
            PipeReader payloadSource,
            PipeReader? payloadSourceStream,
            PipeWriter payloadSink,
            CancellationToken cancel)
        {
            try
            {
                await SendPayloadAsyncCore(payloadSource, payloadSourceStream == null).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                if (payloadSourceStream != null)
                {
                    await payloadSourceStream.CompleteAsync(exception).ConfigureAwait(false);
                }
            }

            if (payloadSourceStream != null)
            {
                _ = Task.Run(() => SendPayloadAsyncCore(payloadSourceStream, true), CancellationToken.None);
            }

            async Task SendPayloadAsyncCore(PipeReader source, bool completeWhenDone)
            {
                // TODO: XXX
                //frameWriter.CompleteCancellationToken = cancel;
                Exception? completeReason = null;
                FlushResult result;
                try
                {
                    result = await payloadSink.CopyFromAsync(source, completeWhenDone, cancel).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    completeReason = exception;
                }

                await source.CompleteAsync(completeReason).ConfigureAwait(false);
                if (completeWhenDone || completeReason != null)
                {
                    await payloadSink.CompleteAsync(completeReason).ConfigureAwait(false);
                }
            }

            // if (payloadSourceStream == null)
            // {
            //     await outgoingFrame.PayloadSource.CompleteAsync().ConfigureAwait(false);
            //     await payloadSink.CompleteAsync().ConfigureAwait(false);
            // }
            // else
            // {
            //     // TODO: XXX
            //     // TODO: better cancellation token?
            //     // cancel = CancellationToken.None;
            //     // frameWriter.CompleteCancellationToken = cancel;

            //     // send payloadSourceStream in the background
            //     _ = Task.Run(
            //         async () =>
            //         {
            //             try
            //             {
            //                 _ = await payloadSink.CopyFromAsync(
            //                     payloadSourceStream,
            //                     completeWhenDone: true,
            //                     cancel).ConfigureAwait(false);

            //                 await payloadSourceStream.CompleteAsync().ConfigureAwait(false);
            //                 await payloadSink.CompleteAsync().ConfigureAwait(false);
            //             }
            //             catch (Exception exception)
            //             {
            //                 await payloadSourceStream.CompleteAsync(exception).ConfigureAwait(false);
            //                 await payloadSink.CompleteAsync(exception).ConfigureAwait(false);
            //             }
            //         },
            //         cancel);
            // }
        }

        private async Task WaitForShutdownAsync()
        {
            // Receive and decode GoAway frame
            ReadOnlyMemory<byte> buffer = await ReceiveControlFrameAsync(
                IceRpcControlFrameType.GoAway,
                CancellationToken.None).ConfigureAwait(false);

            IceRpcGoAwayBody goAwayFrame = DecodeIceRpcGoAwayBody(buffer);

            // Raise the peer shutdown initiated event.
            try
            {
                PeerShutdownInitiated?.Invoke(goAwayFrame.Message);
            }
            catch (Exception ex)
            {
                Debug.Assert(false, $"{nameof(PeerShutdownInitiated)} raised unexpected exception\n{ex}");
            }

            IEnumerable<IMultiplexedStream> invocations;
            bool alreadyShuttingDown = false;
            lock (_mutex)
            {
                if (_shutdown)
                {
                    alreadyShuttingDown = true;
                }
                else
                {
                    // Mark the connection as shutdown to prevent further requests from being accepted.
                    _shutdown = true;
                    if (_invocations.Count == 0 && _dispatches.Count == 0)
                    {
                        _dispatchesAndInvocationsCompleted.SetResult();
                    }
                }

                // Cancel the invocations that were not dispatched by the peer.
                invocations = _invocations.Where(stream =>
                    !stream.IsStarted ||
                    (stream.Id > (stream.IsBidirectional ?
                        goAwayFrame.LastBidirectionalStreamId :
                        goAwayFrame.LastUnidirectionalStreamId))).ToArray();
            }

            foreach (IMultiplexedStream stream in invocations)
            {
                stream.Abort(MultiplexedStreamError.ConnectionShutdownByPeer);
            }

            if (!alreadyShuttingDown)
            {
                // Send GoAway frame if not already shutting down.
                await SendControlFrameAsync(
                    IceRpcControlFrameType.GoAway,
                    (ref IceEncoder encoder) => new IceRpcGoAwayBody(
                        _lastRemoteBidirectionalStreamId,
                        _lastRemoteUnidirectionalStreamId,
                        goAwayFrame.Message).Encode(ref encoder),
                    CancellationToken.None).ConfigureAwait(false);
            }

            try
            {
                // Wait for dispatches and invocations to complete.
                await _dispatchesAndInvocationsCompleted.Task.WaitAsync(
                    _waitForShutdownCancellationSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Cancel invocations and dispatches to speed up the shutdown.
                IEnumerable<IncomingRequest> dispatches;
                lock (_mutex)
                {
                    invocations = _invocations.ToArray();
                    dispatches = _dispatches.ToArray();
                }

                foreach (IncomingRequest request in dispatches)
                {
                    try
                    {
                        request.CancelDispatchSource!.Cancel();
                    }
                    catch (ObjectDisposedException)
                    {
                        // Ignore, the dispatch completed concurrently.
                    }
                }

                foreach (IMultiplexedStream stream in invocations)
                {
                    stream.Abort(MultiplexedStreamError.ConnectionShutdown);
                }

                // Wait again for dispatches and invocations to complete.
                await _dispatchesAndInvocationsCompleted.Task.ConfigureAwait(false);
            }

            // We are done with the shutdown, notify the peer that shutdown completed on our side.
            await SendControlFrameAsync(
                IceRpcControlFrameType.GoAwayCompleted,
                frameEncodeAction: null,
                CancellationToken.None).ConfigureAwait(false);

            // Wait for the peer to complete its side of the shutdown.
            buffer = await ReceiveControlFrameAsync(
                IceRpcControlFrameType.GoAwayCompleted,
                CancellationToken.None).ConfigureAwait(false);
            if (!buffer.IsEmpty)
            {
                throw new InvalidDataException($"{nameof(IceRpcControlFrameType.GoAwayCompleted)} frame is not empty");
            }

            IceRpcGoAwayBody DecodeIceRpcGoAwayBody(ReadOnlyMemory<byte> buffer)
            {
                var decoder = new IceDecoder(buffer, Encoding.Slice20);
                return new IceRpcGoAwayBody(ref decoder);
            }

        }
    }
}
