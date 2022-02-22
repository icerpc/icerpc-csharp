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
        private bool _shutdownCanceled;
        private readonly CancellationTokenSource _waitForShutdownCancellationSource = new();

        public void Dispose() => _waitForShutdownCancellationSource.Dispose();

        public Task PingAsync(CancellationToken cancel) =>
            SendControlFrameAsync(IceRpcControlFrameType.Ping, null, cancel);

        /// <inheritdoc/>
        public async Task<IncomingRequest> ReceiveRequestAsync()
        {
            CancellationToken cancel = _receiveRequestCancellationToken;

            while (true)
            {
                // Accepts a new stream.
                IMultiplexedStream stream;
                try
                {
                    stream = await _networkConnection.AcceptStreamAsync(cancel).ConfigureAwait(false);
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
                IDictionary<int, ReadOnlyMemory<byte>> fields;
                FeatureCollection features = FeatureCollection.Empty;
                PipeReader reader = stream.Input;
                try
                {
                    ReadResult readResult = await reader.ReadSegmentAsync(cancel).ConfigureAwait(false);

                    if (readResult.Buffer.IsEmpty)
                    {
                        throw new InvalidDataException($"received icerpc request with empty header");
                    }

                    (header, fields) = DecodeHeader(readResult.Buffer);
                    reader.AdvanceTo(readResult.Buffer.End);

                    // Decode Context from Fields and set corresponding feature.
                    if (fields.DecodeValue(
                        (int)FieldKey.Context,
                        (ref SliceDecoder decoder) => decoder.DecodeDictionary(
                            minKeySize: 1,
                            minValueSize: 1,
                            size => new Dictionary<string, string>(size),
                            keyDecodeFunc: (ref SliceDecoder decoder) => decoder.DecodeString(),
                            valueDecodeFunc: (ref SliceDecoder decoder) => decoder.DecodeString()))
                                is Dictionary<string, string> context && context.Count > 0)
                    {
                        features = features.WithContext(context);
                    }
                }
                catch (Exception ex)
                {
                    await stream.Input.CompleteAsync(ex).ConfigureAwait(false);
                    await stream.Output.CompleteAsync(ex).ConfigureAwait(false);
                    throw;
                }

                var request = new IncomingRequest(
                    Protocol.IceRpc,
                    path: header.Path,
                    fragment: "", // no fragment with icerpc
                    operation: header.Operation,
                    payload: reader,
                    payloadEncoding: header.PayloadEncoding.Length > 0 ?
                        Encoding.FromString(header.PayloadEncoding) : IceRpcDefinitions.Encoding,
                    responseWriter: stream.IsBidirectional ? stream.Output : InvalidPipeWriter.Instance)
                {
                    IsOneway = !stream.IsBidirectional,
                    Features = features,
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

                static (IceRpcRequestHeader, IDictionary<int, ReadOnlyMemory<byte>>) DecodeHeader(
                    ReadOnlySequence<byte> buffer)
                {
                    var decoder = new SliceDecoder(buffer, Encoding.Slice20);
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
            IDictionary<int, ReadOnlyMemory<byte>> fields;

            PipeReader responseReader = request.ResponseReader;

            try
            {
                ReadResult readResult = await responseReader.ReadSegmentAsync(cancel).ConfigureAwait(false);

                if (readResult.IsCanceled)
                {
                    lock (_mutex)
                    {
                        if (_shutdownCanceled)
                        {
                            // If shutdown is canceled, pending invocations are canceled.
                            throw new OperationCanceledException("shutdown canceled");
                        }
                        else if (_shutdown)
                        {
                            // It shutdown isn't canceled, the cancelation indicates that the peer didn't dispatch the
                            // invocation.
                            throw new ConnectionClosedException("connection shutdown by peer");
                        }
                        else
                        {
                            // The stream was aborted (occurs if the Slic connection is disposed).
                            throw new ConnectionLostException();
                        }
                    }
                }

                if (readResult.Buffer.IsEmpty)
                {
                    throw new InvalidDataException($"received icerpc response with empty header");
                }

                (header, fields) = DecodeHeader(readResult.Buffer);
                responseReader.AdvanceTo(readResult.Buffer.End);

                RetryPolicy? retryPolicy = fields.DecodeValue(
                    (int)FieldKey.RetryPolicy, (ref SliceDecoder decoder) => new RetryPolicy(ref decoder));
                if (retryPolicy != null)
                {
                    request.Features = request.Features.With(retryPolicy);
                }
            }
            catch (MultiplexedStreamAbortedException ex)
            {
                await responseReader.CompleteAsync(ex).ConfigureAwait(false);
                if (ex.ErrorKind == MultiplexedStreamErrorKind.Protocol)
                {
                    throw ex.ToIceRpcException();
                }
                else
                {
                    throw new ConnectionLostException(ex);
                }
            }
            catch (OperationCanceledException)
            {
                // Notify the peer that we give up on receiving the response. The peer will cancel the dispatch upon
                // receiving this notification.
                await responseReader.CompleteAsync(
                    IceRpcStreamError.InvocationCanceled.ToException()).ConfigureAwait(false);
                throw;
            }
            catch (Exception ex)
            {
                await responseReader.CompleteAsync(ex).ConfigureAwait(false);
                throw;
            }

            return new IncomingResponse(
                request,
                header.ResultType,
                payload: responseReader)
            {
                Fields = fields,
            };

            static (IceRpcResponseHeader, IDictionary<int, ReadOnlyMemory<byte>>) DecodeHeader(
                ReadOnlySequence<byte> buffer)
            {
                var decoder = new SliceDecoder(buffer, Encoding.Slice20);
                return (new IceRpcResponseHeader(ref decoder), decoder.DecodeFieldDictionary());
            }
        }

        /// <inheritdoc/>
        public async Task SendRequestAsync(OutgoingRequest request, CancellationToken cancel)
        {
            if (request.Proxy.Fragment.Length > 0)
            {
                throw new NotSupportedException("the icerpc protocol does not support fragments");
            }

            IMultiplexedStream? stream = null;
            try
            {
                // Create the stream.
                stream = _networkConnection.CreateStream(!request.IsOneway);
                request.InitialPayloadSink.SetDecoratee(stream.Output);

                // Keep track of the invocation for the shutdown logic.
                if (!request.IsOneway || request.PayloadSourceStream != null)
                {
                    bool shutdown = false;
                    lock (_mutex)
                    {
                        if (_shutdown)
                        {
                            shutdown = true;
                        }
                        else
                        {
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

                    if (shutdown)
                    {
                        throw new ConnectionClosedException("connection shutdown");
                    }
                }

                EncodeHeader(stream.Output);
                await SendPayloadAsync(request, cancel).ConfigureAwait(false);

                request.IsSent = true;

                if (!request.IsOneway)
                {
                    request.ResponseReader = stream.Input;
                }
            }
            catch (Exception ex)
            {
                await request.PayloadSource.CompleteAsync(ex).ConfigureAwait(false);
                if (request.PayloadSourceStream != null)
                {
                    await request.PayloadSourceStream.CompleteAsync(ex).ConfigureAwait(false);
                }
                await request.PayloadSink.CompleteAsync(ex).ConfigureAwait(false);

                if (stream != null)
                {
                    await stream.Input.CompleteAsync(ex).ConfigureAwait(false);
                    await stream.Output.CompleteAsync(ex).ConfigureAwait(false);
                }
                throw;
            }

            void EncodeHeader(PipeWriter writer)
            {
                var encoder = new SliceEncoder(writer, Encoding.Slice20);

                // Write the IceRpc request header.
                Memory<byte> sizePlaceholder = encoder.GetPlaceholderMemory(2);
                int headerStartPos = encoder.EncodedByteCount; // does not include the size

                var header = new IceRpcRequestHeader(
                    request.Proxy.Path,
                    request.Operation,
                    request.PayloadEncoding == IceRpcDefinitions.Encoding ? "" : request.PayloadEncoding.ToString());

                header.Encode(ref encoder);

                // If the context feature is set, convert it into a FieldsOverrides entry. This will overwrite any
                // existing entry.
                // We cannot use request.Features.GetContext here because it doesn't distinguish between empty and not
                // set context.
                if (request.Features.Get<Context>()?.Value is IDictionary<string, string> context)
                {
                    if (context.Count == 0)
                    {
                        // make sure it's not set anywhere
                        request.Fields = request.Fields.Without((int)FieldKey.Context);
                        request.FieldsOverrides = request.FieldsOverrides.Without((int)FieldKey.Context);
                    }
                    else
                    {
                        request.FieldsOverrides = request.FieldsOverrides.With(
                            (int)FieldKey.Context,
                            (ref SliceEncoder encoder) => encoder.EncodeDictionary(
                                context,
                                (ref SliceEncoder encoder, string value) => encoder.EncodeString(value),
                                (ref SliceEncoder encoder, string value) => encoder.EncodeString(value)));
                    }
                }

                encoder.EncodeFieldDictionary(request.FieldsOverrides, request.Fields);

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

            try
            {
                EncodeHeader();
                await SendPayloadAsync(response, cancel).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await response.PayloadSource.CompleteAsync(ex).ConfigureAwait(false);
                if (response.PayloadSourceStream != null)
                {
                    await response.PayloadSourceStream.CompleteAsync(ex).ConfigureAwait(false);
                }
                await response.PayloadSink.CompleteAsync(ex).ConfigureAwait(false);
                throw;
            }

            void EncodeHeader()
            {
                var encoder = new SliceEncoder(request.ResponseWriter, Encoding.Slice20);

                // Write the IceRpc response header.
                Memory<byte> sizePlaceholder = encoder.GetPlaceholderMemory(2);
                int headerStartPos = encoder.EncodedByteCount;

                new IceRpcResponseHeader(response.ResultType).Encode(ref encoder);
                encoder.EncodeFieldDictionary(response.FieldsOverrides, response.Fields);

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
                    (ref SliceEncoder encoder) => new IceRpcGoAwayBody(
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
                (ref SliceEncoder encoder) =>
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
                var decoder = new SliceDecoder(buffer, Encoding.Slice20);
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
            while (true)
            {
                ReadResult readResult = await _remoteControlStream!.Input.ReadSegmentAsync(
                    cancel).ConfigureAwait(false);
                try
                {
                    if (readResult.Buffer.Length == 0)
                    {
                        throw new InvalidDataException("invalid empty control frame");
                    }

                    IceRpcControlFrameType frameType = readResult.Buffer.FirstSpan[0].AsIceRpcControlFrameType();
                    if (frameType == IceRpcControlFrameType.Ping)
                    {
                        // expected, nothing to do
                        if (readResult.Buffer.Length > 1)
                        {
                            throw new InvalidDataException("invalid non-empty ping control frame");
                        }
                    }
                    else if (frameType != expectedFrameType)
                    {
                        throw new InvalidDataException(
                            $"received frame type {frameType} but expected {expectedFrameType}");
                    }
                    else
                    {
                        return readResult.Buffer.Slice(1).ToArray();
                    }
                }
                finally
                {
                    _remoteControlStream!.Input.AdvanceTo(readResult.Buffer.End);
                }
            }
        }

        private async Task SendControlFrameAsync(
            IceRpcControlFrameType frameType,
            EncodeAction? frameEncodeAction,
            CancellationToken cancel)
        {
            EncodeFrame(_controlStream!.Output);

            if (frameType == IceRpcControlFrameType.GoAwayCompleted)
            {
                await _controlStream!.Output.WriteAsync(
                    ReadOnlySequence<byte>.Empty,
                    completeWhenDone: true,
                    cancel).ConfigureAwait(false);
            }
            else
            {
                await _controlStream!.Output.FlushAsync(cancel).ConfigureAwait(false);
            }

            void EncodeFrame(IBufferWriter<byte> bufferWriter)
            {
                var encoder = new SliceEncoder(bufferWriter, Encoding.Slice20);
                Memory<byte> sizePlaceholder = encoder.GetPlaceholderMemory(4); // TODO: reduce bytes?
                int startPos = encoder.EncodedByteCount; // does not include the size
                encoder.EncodeByte((byte)frameType);
                frameEncodeAction?.Invoke(ref encoder);
                Slice20Encoding.EncodeSize(encoder.EncodedByteCount - startPos, sizePlaceholder.Span);
            }
        }

        /// <summary>Sends the payload source and payload source stream of an outgoing frame.</summary>
        private static async ValueTask SendPayloadAsync(OutgoingFrame outgoingFrame, CancellationToken cancel)
        {
            FlushResult flushResult = await outgoingFrame.PayloadSink.CopyFromAsync(
                outgoingFrame.PayloadSource,
                outgoingFrame.PayloadSourceStream == null,
                cancel).ConfigureAwait(false);

            await outgoingFrame.PayloadSource.CompleteAsync().ConfigureAwait(false);

            Debug.Assert(!flushResult.IsCanceled); // not implemented yet

            if (flushResult.IsCompleted)
            {
                // The remote reader gracefully complete the stream input pipe. TODO: which exception should we
                // throw here? We throw OperationCanceledException... which implies that if the frame is an outgoing
                // request is won't be retried.
                throw new OperationCanceledException("peer stopped reading the payload");
            }

            // The caller calls CompleteAsync on the payload source/sink if an exception is thrown by CopyFromAsync
            // above.

            if (outgoingFrame.PayloadSourceStream != null)
            {
                // Send payloadSourceStream in the background
                _ = Task.Run(
                    async () =>
                    {
                        try
                        {
                            _ = await outgoingFrame.PayloadSink.CopyFromAsync(
                                outgoingFrame.PayloadSourceStream,
                                completeWhenDone: true,
                                CancellationToken.None).ConfigureAwait(false);

                            await outgoingFrame.PayloadSourceStream.CompleteAsync().ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            await outgoingFrame.PayloadSink.CompleteAsync(ex).ConfigureAwait(false);
                            await outgoingFrame.PayloadSourceStream.CompleteAsync(ex).ConfigureAwait(false);
                        }
                    },
                    cancel);
            }
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
                // Cancel the invocation pending read response.
                stream.Input.CancelPendingRead();
            }

            if (!alreadyShuttingDown)
            {
                // Send GoAway frame if not already shutting down.
                await SendControlFrameAsync(
                    IceRpcControlFrameType.GoAway,
                    (ref SliceEncoder encoder) => new IceRpcGoAwayBody(
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
                    _shutdownCanceled = true;
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
                    stream.Input.CancelPendingRead();
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

            static IceRpcGoAwayBody DecodeIceRpcGoAwayBody(ReadOnlyMemory<byte> buffer)
            {
                var decoder = new SliceDecoder(buffer, Encoding.Slice20);
                return new IceRpcGoAwayBody(ref decoder);
            }
        }
    }
}
