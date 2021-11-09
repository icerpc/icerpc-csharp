// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features.Internal;
using IceRpc.Slice;
using IceRpc.Slice.Internal;
using IceRpc.Transports;
using IceRpc.Transports.Internal;
using System.Diagnostics;

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

        private readonly CancellationTokenSource _shutdownCancellationTokenSource = new();
        private readonly TaskCompletionSource _dispatchesAndInvocationsCompleted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private IMultiplexedStream? _controlStream;
        private readonly HashSet<IncomingRequest> _dispatches = new();
        private readonly int _incomingFrameMaxSize;
        private readonly HashSet<OutgoingRequest> _invocations = new();
        private long _lastRemoteBidirectionalStreamId = -1;
        // TODO: to we really need to keep track of this since we don't keep track of one-way requests?
        private long _lastRemoteUnidirectionalStreamId = -1;
        private readonly object _mutex = new();
        private int? _peerIncomingFrameMaxSize;
        private IMultiplexedStream? _remoteControlStream;
        private bool _shutdown;
        private bool _shutdownCanceled;
        private readonly IMultiplexedStreamFactory _streamFactory;

        public void Dispose()
        {
            _shutdownCancellationTokenSource.Cancel();
            _shutdownCancellationTokenSource.Dispose();
        }

        public Task PingAsync(CancellationToken cancel) => SendControlFrameAsync(Ice2FrameType.Ping, null, cancel);

        /// <inheritdoc/>
        public async Task<IncomingRequest> ReceiveRequestAsync()
        {
            CancellationToken cancel = _shutdownCancellationTokenSource.Token;

            while (true)
            {
                IMultiplexedStream stream;

                ReadOnlyMemory<byte> buffer;

                try
                {
                    // Accepts a new stream.
                    stream = await _streamFactory!.AcceptStreamAsync(cancel).ConfigureAwait(false);

                    // Receives the request frame from the stream. TODO: delayed payload receive.
                    buffer = await ReceiveFrameAsync(
                        stream,
                        Ice2FrameType.Request,
                        cancel).ConfigureAwait(false);
                }
                catch
                {
                    lock (_mutex)
                    {
                        if (_shutdown && _invocations.Count == 0 && _dispatches.Count == 0)
                        {
                            // The connection was gracefully shutdown, raise ConnectionClosedException here to ensure
                            // that the ClosedEvent will report this exception instead of the transport failure.
                            throw new ConnectionClosedException("connection gracefully shutdown");
                        }
                    }
                    throw;
                }

                var decoder = new Ice20Decoder(buffer);
                int headerSize = decoder.DecodeSize();
                int headerStartPos = decoder.Pos;

                // We use the generated code for the header body and read the rest of the header "by hand".
                var requestHeaderBody = new Ice2RequestHeaderBody(decoder);
                if (requestHeaderBody.Deadline < -1 || requestHeaderBody.Deadline == 0)
                {
                    throw new InvalidDataException($"received invalid deadline value {requestHeaderBody.Deadline}");
                }

                // Read the fields.
                IReadOnlyDictionary<int, ReadOnlyMemory<byte>> fields = decoder.DecodeFieldDictionary();

                // Ensure we read the same number of bytes as the header size.
                int payloadSize = decoder.DecodeSize();
                if (decoder.Pos - headerStartPos != headerSize)
                {
                    throw new InvalidDataException(
                        @$"received invalid request header: expected {headerSize} bytes but read {
                            decoder.Pos - headerStartPos} bytes");
                }

                // Decode Context from Fields and set corresponding feature.
                FeatureCollection features = FeatureCollection.Empty;
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

                ReadOnlyMemory<byte> payload = buffer[decoder.Pos..];
                if (payloadSize != payload.Length)
                {
                    throw new InvalidDataException(
                        @$"request payload size mismatch: expected {payloadSize} bytes, read {
                            payload.Length} bytes");
                }

                if (requestHeaderBody.Operation.Length == 0)
                {
                    throw new InvalidDataException("received request with empty operation name");
                }

                var request = new IncomingRequest(
                    Protocol.Ice2,
                    path: requestHeaderBody.Path,
                    operation: requestHeaderBody.Operation)
                {
                    IsIdempotent = requestHeaderBody.Idempotent ?? false,
                    IsOneway = !stream.IsBidirectional,
                    Features = features,
                    // The infinite deadline is encoded as -1 and converted to DateTime.MaxValue
                    Deadline = requestHeaderBody.Deadline == -1 ?
                        DateTime.MaxValue : DateTime.UnixEpoch + TimeSpan.FromMilliseconds(requestHeaderBody.Deadline),
                    PayloadEncoding = requestHeaderBody.PayloadEncoding is string payloadEncoding ?
                        Encoding.FromString(payloadEncoding) : Ice2Definitions.Encoding,
                    Fields = fields,
                    Payload = payload,
                    Stream = stream
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
            if (request.Stream == null)
            {
                throw new InvalidOperationException($"{nameof(request.Stream)} is not set");
            }
            else if (request.IsOneway)
            {
                throw new InvalidOperationException("can't receive a response for a one-way request");
            }

            try
            {
                ReadOnlyMemory<byte> buffer =
                    await ReceiveFrameAsync(request.Stream, Ice2FrameType.Response, cancel).ConfigureAwait(false);

                var decoder = new Ice20Decoder(buffer);
                int headerSize = decoder.DecodeSize();
                int headerStartPos = decoder.Pos;

                var responseHeaderBody = new Ice2ResponseHeaderBody(decoder);
                IReadOnlyDictionary<int, ReadOnlyMemory<byte>> fields = decoder.DecodeFieldDictionary();
                int payloadSize = decoder.DecodeSize();
                if (decoder.Pos - headerStartPos != headerSize)
                {
                    throw new InvalidDataException(
                        @$"received invalid response header: expected {headerSize} bytes but read {
                            decoder.Pos - headerStartPos} bytes");
                }

                Encoding payloadEncoding = responseHeaderBody.PayloadEncoding is string encoding ?
                    Encoding.FromString(encoding) : Ice2Definitions.Encoding;

                FeatureCollection features = FeatureCollection.Empty;
                RetryPolicy? retryPolicy = fields.Get((int)FieldKey.RetryPolicy, decoder => new RetryPolicy(decoder));
                if (retryPolicy != null)
                {
                    features = new();
                    features.Set(retryPolicy);
                }

                var response = new IncomingResponse(Protocol.Ice2, responseHeaderBody.ResultType)
                {
                    Features = features,
                    Fields = fields,
                    PayloadEncoding = payloadEncoding,
                    Payload = buffer[decoder.Pos..],
                };

                if (payloadSize != response.Payload.Length)
                {
                    throw new InvalidDataException(
                        @$"response payload size mismatch: expected {payloadSize} bytes, read
                            {response.Payload.Length} bytes");
                }

                return response;
            }
            catch (StreamAbortedException ex)
            {
                switch (ex.ErrorCode)
                {
                    case StreamError.ConnectionAborted:
                        throw new ConnectionLostException(ex);

                    case StreamError.ConnectionShutdown:
                        throw new OperationCanceledException("connection shutdown", ex);

                    case StreamError.ConnectionShutdownByPeer:
                        throw new ConnectionClosedException("connection shutdown by peer", ex);

                    case StreamError.DispatchCanceled:
                        throw new OperationCanceledException("dispatch canceled by peer", ex);

                    default:
                        throw;
                }
            }
        }

        /// <inheritdoc/>
        public async Task SendRequestAsync(OutgoingRequest request, CancellationToken cancel)
        {
            // Create the stream.
            request.Stream = _streamFactory!.CreateStream(!request.IsOneway);

            if (!request.IsOneway || request.StreamParamSender != null)
            {
                lock (_mutex)
                {
                    if (_shutdown)
                    {
                        request.Features = request.Features.With(RetryPolicy.Immediately);
                        request.Stream.Abort(StreamError.ConnectionShutdown);
                        throw new ConnectionClosedException("connection shutdown");
                    }
                    _invocations.Add(request);

                    request.Stream.ShutdownAction = () =>
                        {
                            lock (_mutex)
                            {
                                _invocations.Remove(request);

                                // If no more invocations or dispatches and shutting down, shutdown can complete.
                                if (_shutdown && _invocations.Count == 0 && _dispatches.Count == 0)
                                {
                                    _dispatchesAndInvocationsCompleted.SetResult();
                                }
                            }
                        };
                }
            }

            try
            {
                var bufferWriter = new BufferWriter();
                bufferWriter.WriteByteSpan(request.Stream.TransportHeader.Span);
                var encoder = new Ice20Encoder(bufferWriter);

                // Write the Ice2 request header.

                encoder.EncodeIce2FrameType(Ice2FrameType.Request);

                // TODO: simplify sizes, we should be able to remove one of the sizes (the frame size, the
                // frame header size).
                BufferWriter.Position frameSizeStart = encoder.StartFixedLengthSize();
                BufferWriter.Position frameHeaderStart = encoder.StartFixedLengthSize(2);

                // DateTime.MaxValue represents an infinite deadline and it is encoded as -1
                long deadline = request.Deadline == DateTime.MaxValue ? -1 :
                        (long)(request.Deadline - DateTime.UnixEpoch).TotalMilliseconds;

                var requestHeaderBody = new Ice2RequestHeaderBody(
                    request.Path,
                    request.Operation,
                    request.IsIdempotent ? true : null,
                    deadline,
                    request.PayloadEncoding == Ice2Definitions.Encoding ? null : request.PayloadEncoding.ToString());

                requestHeaderBody.Encode(encoder);

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
                encoder.EncodeSize(request.PayloadSize);

                // We're done with the header encoding, write the header size.
                int headerSize = encoder.EndFixedLengthSize(frameHeaderStart, 2);

                // We're done with the frame encoding, write the frame size.
                int frameSize = headerSize + 2 + request.PayloadSize;
                encoder.EncodeFixedLengthSize(frameSize, frameSizeStart);
                if (frameSize > _peerIncomingFrameMaxSize)
                {
                    throw new ArgumentException(
                        $@"the request size ({frameSize} bytes) is larger than the peer's IncomingFrameMaxSize ({
                        _peerIncomingFrameMaxSize} bytes)",
                        nameof(request));
                }

                // Add the payload to the buffer writer.
                bufferWriter.Add(request.Payload);

                // Send the request frame.
                await request.Stream.WriteAsync(bufferWriter.Finish(),
                                                endStream: request.StreamParamSender == null,
                                                cancel).ConfigureAwait(false);

                // Mark the request as sent.
                request.IsSent = true;
            }
            catch (StreamAbortedException ex)
            {
                switch (ex.ErrorCode)
                {
                    case StreamError.ConnectionAborted:
                    case StreamError.StreamAborted:
                        throw new ConnectionLostException(ex);

                    case StreamError.ConnectionShutdown:
                        throw new OperationCanceledException("connection shutdown", ex);

                    default:
                        throw;
                }
            }

            // If there's a stream param sender, we can start sending the data.
            if (request.StreamParamSender != null)
            {
                request.SendStreamParam(request.Stream);
            }
        }

        /// <inheritdoc/>
        public async Task SendResponseAsync(
            OutgoingResponse response,
            IncomingRequest request,
            CancellationToken cancel)
        {
            if (request.Stream == null)
            {
                throw new InvalidOperationException($"{nameof(request.Stream)} is not set");
            }
            else if (request.IsOneway)
            {
                return;
            }

            var bufferWriter = new BufferWriter();
            bufferWriter.WriteByteSpan(request.Stream.TransportHeader.Span);
            var encoder = new Ice20Encoder(bufferWriter);

            // Write the Ice2 response header.
            encoder.EncodeIce2FrameType(Ice2FrameType.Response);

            // TODO: simplify sizes, we should be able to remove one of the sizes (the frame size or the
            // frame header size).
            BufferWriter.Position frameSizeStart = encoder.StartFixedLengthSize();
            BufferWriter.Position frameHeaderStart = encoder.StartFixedLengthSize(2);

            new Ice2ResponseHeaderBody(
                response.ResultType,
                response.PayloadEncoding == Ice2Definitions.Encoding ? null :
                    response.PayloadEncoding.ToString()).Encode(encoder);

            encoder.EncodeFields(response.Fields, response.FieldsDefaults);
            encoder.EncodeSize(response.PayloadSize);

            // We're done with the header encoding, write the header size.
            int headerSize = encoder.EndFixedLengthSize(frameHeaderStart, 2);

            // We're done with the frame encoding, write the frame size.
            int frameSize = headerSize + 2 + response.PayloadSize;
            encoder.EncodeFixedLengthSize(frameSize, frameSizeStart);
            if (frameSize > _peerIncomingFrameMaxSize)
            {
                // Throw a remote exception instead of this response, the Ice connection will catch it and send it
                // as the response instead of sending this response which is too large.
                throw new DispatchException(
                    $@"the response size ({frameSize} bytes) is larger than IncomingFrameMaxSize ({
                        _peerIncomingFrameMaxSize} bytes)");
            }

            // Add the payload to the buffer writer.
            bufferWriter.Add(response.Payload);

            // Send the response frame.
            await request.Stream.WriteAsync(bufferWriter.Finish(),
                                            endStream: response.StreamParamSender == null,
                                            cancel).ConfigureAwait(false);

            // If there's a stream param sender, we can start sending the data.
            if (response.StreamParamSender != null)
            {
                response.SendStreamParam(request.Stream);
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

            if (_shutdownCanceled)
            {
                // If shutdown has been canceled, cancel invocations and dispatch now.
                CancelInvocationsAndDispatch(StreamError.ConnectionShutdown);
            }

            if (!alreadyShuttingDown)
            {
                // Send GoAway frame
                await SendControlFrameAsync(
                    Ice2FrameType.GoAway,
                    encoder => new Ice2GoAwayBody(
                        _lastRemoteBidirectionalStreamId,
                        _lastRemoteUnidirectionalStreamId,
                        message).Encode(encoder),
                    cancel).ConfigureAwait(false);
            }

            // Wait for the control streams to complete. The control streams are terminated once the peer closes the
            // the connection.
            await _controlStream!.WaitForShutdownAsync(cancel).ConfigureAwait(false);
            await _remoteControlStream!.WaitForShutdownAsync(cancel).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public void ShutdownCanceled()
        {
            lock (_mutex)
            {
                // If shutdown wasn't called yet, delay the cancellation until ShutdownAsync is called (this can occur
                // if the application cancels ShutdownAsync immediately or before ShutdownAsync is called on the
                // protocol connection). Otherwise, cancel the dispatches and invocations now.
                if (!_shutdown)
                {
                    _shutdownCanceled = true;
                    return;
                }
            }

            CancelInvocationsAndDispatch(StreamError.ConnectionShutdown);
        }

        internal Ice2ProtocolConnection(IMultiplexedStreamFactory streamFactory, int incomingFrameMaxSize)
        {
            _streamFactory = streamFactory;
            _incomingFrameMaxSize = incomingFrameMaxSize;
        }

        internal async Task InitializeAsync(CancellationToken cancel)
        {
            // Create the control stream and send the protocol initialize frame
            _controlStream = _streamFactory.CreateStream(false);

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
            _remoteControlStream = await _streamFactory.AcceptStreamAsync(cancel).ConfigureAwait(false);

            ReadOnlyMemory<byte> buffer = await ReceiveFrameAsync(
                _remoteControlStream,
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
                    try
                    {
                        await WaitForShutdownAsync(_shutdownCancellationTokenSource.Token).ConfigureAwait(false);
                    }
                    finally
                    {
                        // Unblock ReceiveRequest. The Connection will close the exception upon getting an exception
                        // from ReceiveRequest.
                        _shutdownCancellationTokenSource.Cancel();
                    }
                },
                _shutdownCancellationTokenSource.Token);
        }

        private void CancelInvocationsAndDispatch(StreamError streamError)
        {
            IEnumerable<OutgoingRequest> invocations = Enumerable.Empty<OutgoingRequest>();
            IEnumerable<IncomingRequest> dispatches = Enumerable.Empty<IncomingRequest>();

            lock (_mutex)
            {
                if (_invocations.Count > 0)
                {
                    invocations = _invocations.ToArray();
                }
                if (_dispatches.Count > 0)
                {
                    dispatches = _dispatches.ToArray();
                }
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

            foreach (OutgoingRequest request in invocations)
            {
                request.Stream!.Abort(streamError);
            }
        }

        private async ValueTask<ReadOnlyMemory<byte>> ReceiveFrameAsync(
            IMultiplexedStream stream,
            Ice2FrameType expectedFrameType,
            CancellationToken cancel)
        {
            byte[] bufferArray = new byte[256];
            while (true)
            {
                var buffer = new Memory<byte>(bufferArray);

                // Read the frame type and first byte of the size.
                await stream.ReadUntilFullAsync(buffer[0..2], cancel).ConfigureAwait(false);
                var frameType = (Ice2FrameType)buffer.Span[0];
                if (frameType > Ice2FrameType.GoAwayCompleted)
                {
                    throw new InvalidDataException($"invalid Ice2 frame type {frameType}");
                }

                // Read the remainder of the size if needed.
                int sizeLength = Ice20Decoder.DecodeSizeLength(buffer.Span[1]);
                if (sizeLength > 1)
                {
                    await stream.ReadUntilFullAsync(buffer.Slice(2, sizeLength - 1), cancel).ConfigureAwait(false);
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
                    await stream.ReadUntilFullAsync(buffer, cancel).ConfigureAwait(false);
                }
                else
                {
                    buffer = Memory<byte>.Empty;
                }

                if (frameType == Ice2FrameType.Ping)
                {
                    if (stream != _remoteControlStream)
                    {
                        throw new InvalidDataException($"the {frameType} frame is only supported on the control stream");
                    }
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
            var bufferWriter = new BufferWriter(new byte[1024]);
            bufferWriter.WriteByteSpan(_controlStream!.TransportHeader.Span);
            var encoder = new Ice20Encoder(bufferWriter);
            encoder.EncodeByte((byte)frameType);
            BufferWriter.Position sizePos = encoder.StartFixedLengthSize();
            frameEncodeAction?.Invoke(encoder);
            encoder.EndFixedLengthSize(sizePos);
            await _controlStream!.WriteAsync(bufferWriter.Finish(), false, cancel).ConfigureAwait(false);
        }

        private async Task WaitForShutdownAsync(CancellationToken cancel)
        {
            // Receive and decode GoAway frame
            ReadOnlyMemory<byte> buffer = await ReceiveFrameAsync(
                _remoteControlStream!,
                Ice2FrameType.GoAway,
                cancel).ConfigureAwait(false);
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

            IEnumerable<OutgoingRequest> invocations = Enumerable.Empty<OutgoingRequest>();
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
                invocations = _invocations.Where(request =>
                    !request.Stream!.IsStarted ||
                    (request.Stream.Id > (request.Stream!.IsBidirectional ?
                        goAwayFrame.LastBidirectionalStreamId :
                        goAwayFrame.LastUnidirectionalStreamId))).ToArray();
            }

            foreach (OutgoingRequest request in invocations)
            {
                request.Stream!.Abort(StreamError.ConnectionShutdownByPeer);
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
                    cancel).ConfigureAwait(false);
            }

            // Wait for dispatches and invocations to complete.
            await _dispatchesAndInvocationsCompleted.Task.WaitAsync(cancel).ConfigureAwait(false);

            // We are done with the shutdown, notify the peer that shutdown completed on our side.
            await SendControlFrameAsync(
                Ice2FrameType.GoAwayCompleted,
                frameEncodeAction: null,
                cancel).ConfigureAwait(false);

            // Wait for the peer to complete its side of the shutdown.
            buffer = await ReceiveFrameAsync(
                _remoteControlStream!,
                Ice2FrameType.GoAwayCompleted,
                cancel).ConfigureAwait(false);
            if (!buffer.IsEmpty)
            {
                throw new InvalidDataException($"{nameof(Ice2FrameType.GoAwayCompleted)} frame is not empty");
            }
        }
    }
}
