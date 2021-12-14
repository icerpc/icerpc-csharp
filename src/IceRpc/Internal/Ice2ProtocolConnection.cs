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
    internal sealed class Ice2ProtocolConnection : IProtocolConnection
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
        public event Action? PeerShutdownInitiated;

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

        public Task PingAsync(CancellationToken cancel) => SendControlFrameAsync(Ice2FrameType.Ping, null, cancel);

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
                    reader = stream.ToPipeReader(CancellationToken.None);
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

                Ice2RequestHeader header;
                IReadOnlyDictionary<int, ReadOnlyMemory<byte>> fields;
                FeatureCollection features = FeatureCollection.Empty;

                try
                {
                    ReadResult readResult = await reader.ReadSegmentAsync(Encoding.Ice20, cancel).ConfigureAwait(false);

                    // At this point, nothing can call CancelPendingReads on this pipe reader.
                    Debug.Assert(!readResult.IsCanceled);

                    if (readResult.Buffer.IsEmpty)
                    {
                        throw new InvalidDataException($"received ice2 request with empty header");
                    }

                    var decoder = new Ice20Decoder(readResult.Buffer.ToSingleBuffer());
                    header = new Ice2RequestHeader(decoder);
                    fields = decoder.DecodeFieldDictionary();
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
                        decoder => decoder.DecodeDictionary(
                            minKeySize: 1,
                            minValueSize: 1,
                            keyDecodeFunc: decoder => decoder.DecodeString(),
                            valueDecodeFunc: decoder => decoder.DecodeString())) is Dictionary<string, string> context)
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
                    Protocol.Ice2,
                    path: header.Path,
                    operation: header.Operation,
                    payload: reader,
                    payloadEncoding: header.PayloadEncoding.Length > 0 ?
                        Encoding.FromString(header.PayloadEncoding) : Ice2Definitions.Encoding,
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
            }
        }

        /// <inheritdoc/>
        public async Task<IncomingResponse> ReceiveResponseAsync(OutgoingRequest request, CancellationToken cancel)
        {
            // This class sent this request and set ResponseReader on it.
            Debug.Assert(request.ResponseReader != null);
            Debug.Assert(!request.IsOneway);

            Ice2ResponseHeader header;
            IReadOnlyDictionary<int, ReadOnlyMemory<byte>> fields;
            FeatureCollection features = FeatureCollection.Empty;

            PipeReader responseReader = request.ResponseReader;

            try
            {
                ReadResult readResult = await responseReader.ReadSegmentAsync(
                    Encoding.Ice20,
                    cancel).ConfigureAwait(false);

                // At this point, nothing can call CancelPendingReads on this pipe reader.
                Debug.Assert(!readResult.IsCanceled);

                if (readResult.Buffer.IsEmpty)
                {
                    throw new InvalidDataException($"received ice2 response with empty header");
                }

                var decoder = new Ice20Decoder(readResult.Buffer.ToSingleBuffer());
                header = new Ice2ResponseHeader(decoder);
                fields = decoder.DecodeFieldDictionary();
                responseReader.AdvanceTo(readResult.Buffer.End);

                RetryPolicy? retryPolicy = fields.Get(
                    (int)FieldKey.RetryPolicy, decoder => new RetryPolicy(decoder));
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
                Protocol.Ice2,
                header.ResultType,
                responseReader,
                payloadEncoding: header.PayloadEncoding.Length > 0 ?
                    Encoding.FromString(header.PayloadEncoding) : Ice2Definitions.Encoding)
            {
                Features = features,
                Fields = fields,
            };

            return response;
        }

        /// <inheritdoc/>
        public async Task SendRequestAsync(OutgoingRequest request, CancellationToken cancel)
        {
            IMultiplexedStream stream;
            MultiplexedStreamPipeWriter? requestWriter = null;
            try
            {
                // Create the stream.
                stream = _networkConnection.CreateStream(!request.IsOneway);
                requestWriter = new MultiplexedStreamPipeWriter(stream);
                request.InitialPayloadSink.SetDecoratee(requestWriter);

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

                var encoder = new Ice20Encoder(requestWriter);

                // Write the Ice2 request header.
                Memory<byte> sizePlaceHolder = encoder.GetPlaceHolderMemory(2);
                int headerStartPos = encoder.EncodedBytes; // does not include the size

                // DateTime.MaxValue represents an infinite deadline and it is encoded as -1
                long deadline = request.Deadline == DateTime.MaxValue ? -1 :
                        (long)(request.Deadline - DateTime.UnixEpoch).TotalMilliseconds;

                var header = new Ice2RequestHeader(
                    request.Path,
                    request.Operation,
                    request.IsIdempotent,
                    deadline,
                    request.PayloadEncoding == Ice2Definitions.Encoding ? "" : request.PayloadEncoding.ToString());

                header.Encode(encoder);

                // If the context feature is set to a non empty context, or if the fields defaults contains a context
                // entry and the context feature is set, marshal the context feature in the request fields. The context
                // feature must prevail over field defaults. Cannot use request.Features.GetContext it doesn't
                // distinguish between empty an non set context.
                if (request.Features.Get<Context>()?.Value is IDictionary<string, string> context &&
                    (context.Count > 0 || request.FieldsDefaults.ContainsKey((int)FieldKey.Context)))
                {
                    // Encodes context
                    request.Fields[(int)FieldKey.Context] =
                        encoder => encoder.EncodeDictionary(context,
                                                            (encoder, value) => encoder.EncodeString(value),
                                                            (encoder, value) => encoder.EncodeString(value));
                }
                // else context remains empty (not set)

                encoder.EncodeFields(request.Fields, request.FieldsDefaults);

                // We're done with the header encoding, write the header size.
                Ice20Encoder.EncodeSize(encoder.EncodedBytes - headerStartPos, sizePlaceHolder.Span);

                await SendPayloadAsync(request, requestWriter, cancel).ConfigureAwait(false);
                request.IsSent = true;
            }
            catch (Exception ex)
            {
                await request.PayloadSource.CompleteAsync(ex).ConfigureAwait(false);
                await request.PayloadSink.CompleteAsync(ex).ConfigureAwait(false);
                throw;
            }

            if (!request.IsOneway)
            {
                // TODO: is it correct to pass cancel to the new response reader, since it's used for reading the
                // entire payload?
                request.ResponseReader = stream.ToPipeReader(cancel);
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

            var responseWriter = (MultiplexedStreamPipeWriter)request.ResponseWriter;

            try
            {
                var encoder = new Ice20Encoder(responseWriter);

                // Write the Ice2 response header.
                Memory<byte> sizePlaceHolder = encoder.GetPlaceHolderMemory(2);
                int headerStartPos = encoder.EncodedBytes;

                new Ice2ResponseHeader(
                    response.ResultType,
                    response.PayloadEncoding == Ice2Definitions.Encoding ? "" :
                        response.PayloadEncoding.ToString()).Encode(encoder);

                encoder.EncodeFields(response.Fields, response.FieldsDefaults);

                // We're done with the header encoding, write the header size.
                Ice20Encoder.EncodeSize(encoder.EncodedBytes - headerStartPos, sizePlaceHolder.Span);

                await SendPayloadAsync(response, responseWriter, cancel).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await response.PayloadSource.CompleteAsync(ex).ConfigureAwait(false);
                await response.PayloadSink.CompleteAsync(ex).ConfigureAwait(false);
                throw;
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
                    Ice2FrameType.GoAway,
                    encoder => new Ice2GoAwayBody(
                        _lastRemoteBidirectionalStreamId,
                        _lastRemoteUnidirectionalStreamId,
                        message).Encode(encoder),
                    CancellationToken.None).ConfigureAwait(false);
            }

            // Wait for the control streams to complete. The control streams are terminated once the peer closes the
            // the connection.
            await _controlStream!.WaitForShutdownAsync(CancellationToken.None).ConfigureAwait(false);
            await _remoteControlStream!.WaitForShutdownAsync(CancellationToken.None).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        internal Ice2ProtocolConnection(IMultiplexedNetworkConnection networkConnection, int incomingFrameMaxSize)
        {
            _networkConnection = networkConnection;
            _incomingFrameMaxSize = incomingFrameMaxSize;
        }

        internal async Task InitializeAsync(CancellationToken cancel)
        {
            // Create the control stream and send the protocol initialize frame
            _controlStream = _networkConnection.CreateStream(false);

            await SendControlFrameAsync(
                Ice2FrameType.Initialize,
                encoder =>
                {
                    // Encode the transport parameters as Fields
                    encoder.EncodeSize(1);

                    // Transmit out local incoming frame maximum size
                    encoder.EncodeField((int)Ice2ParameterKey.IncomingFrameMaxSize,
                                        (ulong)_incomingFrameMaxSize,
                                        (encoder, value) => encoder.EncodeVarULong(value));
                },
                cancel).ConfigureAwait(false);

            // Wait for the remote control stream to be accepted and read the protocol initialize frame
            _remoteControlStream = await _networkConnection.AcceptStreamAsync(cancel).ConfigureAwait(false);

            ReadOnlyMemory<byte> buffer = await ReceiveControlFrameAsync(
                Ice2FrameType.Initialize,
                cancel).ConfigureAwait(false);

            // Read the protocol parameters which are encoded as IceRpc.Fields.

            var decoder = new Ice20Decoder(buffer);
            int dictionarySize = decoder.DecodeSize();
            for (int i = 0; i < dictionarySize; ++i)
            {
                (int key, ReadOnlyMemory<byte> value) = decoder.DecodeField();
                if (key == (int)Ice2ParameterKey.IncomingFrameMaxSize)
                {
                    checked
                    {
                        _peerIncomingFrameMaxSize = (int)IceDecoder.DecodeVarULong(value.Span).Value;
                    }

                    if (_peerIncomingFrameMaxSize < 1024)
                    {
                        throw new InvalidDataException($@"the peer's IncomingFrameMaxSize ({
                            _peerIncomingFrameMaxSize} bytes) value is inferior to 1KB");
                    }
                }
                else
                {
                    // Ignore unsupported parameters.
                }
            }

            if (_peerIncomingFrameMaxSize == null)
            {
                throw new InvalidDataException("missing IncomingFrameMaxSize Ice2 connection parameter");
            }

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
        }

        private async ValueTask<ReadOnlyMemory<byte>> ReceiveControlFrameAsync(
            Ice2FrameType expectedFrameType,
            CancellationToken cancel)
        {
            byte[] bufferArray = new byte[256];
            while (true)
            {
                var buffer = new Memory<byte>(bufferArray);

                // Read the frame type and first byte of the size.
                await _remoteControlStream!.ReadUntilFullAsync(buffer[0..2], cancel).ConfigureAwait(false);
                var frameType = (Ice2FrameType)buffer.Span[0];
                if (frameType > Ice2FrameType.GoAwayCompleted)
                {
                    throw new InvalidDataException($"invalid Ice2 frame type {frameType}");
                }

                // Read the remainder of the size if needed.
                int sizeLength = Ice20Decoder.DecodeSizeLength(buffer.Span[1]);
                if (sizeLength > 1)
                {
                    await _remoteControlStream!.ReadUntilFullAsync(
                        buffer.Slice(2, sizeLength - 1), cancel).ConfigureAwait(false);
                }

                int frameSize = Ice20Decoder.DecodeSize(buffer[1..].AsReadOnlySpan()).Size;
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

                if (frameType == Ice2FrameType.Ping)
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
            Ice2FrameType frameType,
            Action<Ice20Encoder>? frameEncodeAction,
            CancellationToken cancel)
        {
            Memory<byte> buffer = new byte[1024]; // TODO: use pooled memory?
            var bufferWriter = new SingleBufferWriter(buffer);

            var encoder = new Ice20Encoder(bufferWriter);
            encoder.EncodeByte((byte)frameType);
            Memory<byte> sizePlaceHolder = encoder.GetPlaceHolderMemory(4); // TODO: reduce bytes
            int startPos = encoder.EncodedBytes; // does not include the size
            frameEncodeAction?.Invoke(encoder);
            Ice20Encoder.EncodeSize(encoder.EncodedBytes - startPos, sizePlaceHolder.Span);

            buffer = bufferWriter.WrittenBuffer;

            await _controlStream!.WriteAsync(
                new ReadOnlyMemory<byte>[] { buffer }, // TODO: better API
                frameType == Ice2FrameType.GoAwayCompleted,
                cancel).ConfigureAwait(false);
        }

        /// <summary>Sends the payload source and payload source stream of an outgoing frame.</summary>
        private static async ValueTask SendPayloadAsync(
            OutgoingFrame outgoingFrame,
            MultiplexedStreamPipeWriter frameWriter,
            CancellationToken cancel)
        {
            frameWriter.CompleteCancellationToken = cancel;

            try
            {
                await outgoingFrame.PayloadSource.CopyToAsync(outgoingFrame.PayloadSink, cancel).ConfigureAwait(false);
            }
            catch (TaskCanceledException) // CopyToAsync returns a canceled task if cancel is cancelled
            {
                throw new OperationCanceledException();
            }

            // We need to call FlushAsync in case PayloadSource was empty and CopyToAsync didn't do anything.
            FlushResult flushResult = await outgoingFrame.PayloadSink.FlushAsync(cancel).ConfigureAwait(false);

            await outgoingFrame.PayloadSource.CompleteAsync().ConfigureAwait(false);

            if (!flushResult.IsCompleted && outgoingFrame.PayloadSourceStream is PipeReader payloadSourceStream)
            {
                // send payloadSourceStream in the background
                _ = Task.Run(
                    async () =>
                    {
                        Exception? completeReason = null;

                        // TODO: better cancellation token?
                        CancellationToken cancel = CancellationToken.None;
                        frameWriter.CompleteCancellationToken = cancel;

                        try
                        {
                            await payloadSourceStream.CopyToAsync(
                                outgoingFrame.PayloadSink,
                                cancel).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            completeReason = ex;
                        }

                        await payloadSourceStream.CompleteAsync(completeReason).ConfigureAwait(false);
                        await outgoingFrame.PayloadSink.CompleteAsync(completeReason).ConfigureAwait(false);

                        if (completeReason == null)
                        {
                            // Need to call it again directly on frameWriter in case the PayloadSink.CompleteAsync
                            // ended up calling the no-op frameWriter.Complete.
                            await frameWriter.CompleteAsync().ConfigureAwait(false);
                        }
                    },
                    CancellationToken.None);
            }
            else
            {
                // The implementation of frameWriter.CompleteAsync uses cancel set earlier.
                await outgoingFrame.PayloadSink.CompleteAsync().ConfigureAwait(false);

                // Need to call it again directly on frameWriter in case the PayloadSink.CompleteAsync
                // ended up calling the no-op frameWriter.Complete.
                await frameWriter.CompleteAsync().ConfigureAwait(false);
            }

            // The caller CompleteAsync the payload source/sink if an exception is thrown by CopyToAsync.
        }

        private async Task WaitForShutdownAsync()
        {
            // Receive and decode GoAway frame
            ReadOnlyMemory<byte> buffer = await ReceiveControlFrameAsync(
                Ice2FrameType.GoAway,
                CancellationToken.None).ConfigureAwait(false);
            var goAwayFrame = new Ice2GoAwayBody(new Ice20Decoder(buffer));

            // Raise the peer shutdown initiated event.
            try
            {
                PeerShutdownInitiated?.Invoke();
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
                    Ice2FrameType.GoAway,
                    encoder => new Ice2GoAwayBody(
                        _lastRemoteBidirectionalStreamId,
                        _lastRemoteUnidirectionalStreamId,
                        goAwayFrame.Message).Encode(encoder),
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
                Ice2FrameType.GoAwayCompleted,
                frameEncodeAction: null,
                CancellationToken.None).ConfigureAwait(false);

            // Wait for the peer to complete its side of the shutdown.
            buffer = await ReceiveControlFrameAsync(
                Ice2FrameType.GoAwayCompleted,
                CancellationToken.None).ConfigureAwait(false);
            if (!buffer.IsEmpty)
            {
                throw new InvalidDataException($"{nameof(Ice2FrameType.GoAwayCompleted)} frame is not empty");
            }
        }
    }
}
