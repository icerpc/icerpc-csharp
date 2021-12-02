// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features.Internal;
using IceRpc.Slice;
using IceRpc.Slice.Internal;
using IceRpc.Transports;
using IceRpc.Transports.Internal;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Internal
{
    internal sealed class Ice1ProtocolConnection : IProtocolConnection
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

        private readonly TaskCompletionSource _dispatchesAndInvocationsCompleted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly HashSet<IncomingRequest> _dispatches = new();
        private readonly int _incomingFrameMaxSize;
        private readonly Dictionary<int, OutgoingRequest> _invocations = new();
        private readonly bool _isUdp;
        private readonly object _mutex = new();
        private readonly ISimpleNetworkConnection _networkConnection;
        private int _nextRequestId;
        private readonly TaskCompletionSource _pendingClose = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly AsyncSemaphore _sendSemaphore = new(1);
        private bool _shutdown;

        /// <inheritdoc/>
        public void Dispose()
        {
            // The connection is disposed, if there are sill pending invocations, it indicates a non-graceful shutdown,
            // we raise ConnectionLostException.
            var exception = new ConnectionLostException();

            // Unblock ShutdownAsync which might be waiting for the connection to be disposed.
            _pendingClose.TrySetResult();

            // Unblock invocations which are waiting to be sent.
            _sendSemaphore.Complete(exception);

            // Unblock ShutdownAsync if it's waiting for invocations and dispatches to complete.
            _dispatchesAndInvocationsCompleted.TrySetException(exception);

            CancelInvocations(exception);
            CancelDispatches();
        }

        /// <inheritdoc/>
        public async Task PingAsync(CancellationToken cancel)
        {
            await _sendSemaphore.EnterAsync(cancel).ConfigureAwait(false);
            try
            {
                await _networkConnection.WriteAsync(Ice1Definitions.ValidateConnectionFrame, cancel).ConfigureAwait(false);
            }
            finally
            {
                _sendSemaphore.Release();
            }
        }

        /// <inheritdoc/>
        public async Task<IncomingRequest> ReceiveRequestAsync()
        {
            while (true)
            {
                // Wait for a request frame to be received.
                int requestId;
                Memory<byte> buffer;
                try
                {
                    (requestId, buffer) = await ReceiveFrameAsync().ConfigureAwait(false);
                }
                catch (ConnectionLostException)
                {
                    lock (_mutex)
                    {
                        // The connection was gracefully shut down, raise ConnectionClosedException here to ensure
                        // that the ClosedEvent will report this exception instead of the transport failure.
                        if (_shutdown && _invocations.Count == 0 && _dispatches.Count == 0)
                        {
                            throw new ConnectionClosedException("connection gracefully shut down");
                        }
                        else
                        {
                            throw;
                        }
                    }
                }

                var decoder = new Ice11Decoder(buffer);

                var requestHeader = new Ice1RequestHeader(decoder);
                if (requestHeader.IdentityAndFacet.Identity.Name.Length == 0)
                {
                    throw new InvalidDataException("received ice1 request with empty identity name");
                }
                if (requestHeader.Operation.Length == 0)
                {
                    throw new InvalidDataException("received request with empty operation name");
                }

                // The payload plus 4 bytes from the encapsulation header used to store the payload size encoded with
                // payload encoding.
                Memory<byte> payload = buffer[(decoder.Pos - 4)..];

                // The payload size is the encapsulation size less the 6 bytes of the encapsulation header.
                int payloadSize = requestHeader.EncapsulationSize - 6;
                if (payloadSize != payload.Length - 4)
                {
                    throw new InvalidDataException(@$"request payload size mismatch: expected {payloadSize
                        } bytes, read {payload.Length - 4} bytes");
                }

                var payloadEncoding = Encoding.FromMajorMinor(
                    requestHeader.PayloadEncodingMajor,
                    requestHeader.PayloadEncodingMinor);

               EncodePayloadSize(payloadSize, payloadEncoding, payload.Span[0..4]);

                var request = new IncomingRequest(
                    Protocol.Ice1,
                    path: requestHeader.IdentityAndFacet.ToPath(),
                    operation: requestHeader.Operation,
                    payload: PipeReader.Create(new ReadOnlySequence<byte>(payload)),
                    payloadEncoding)
                {
                    IsIdempotent = requestHeader.OperationMode != OperationMode.Normal,
                    IsOneway = requestId == 0,
                    Deadline = DateTime.MaxValue
                };

                request.Features = request.Features.With(new Ice1Request(requestId, outgoing: false));
                if (requestHeader.Context.Count > 0)
                {
                    request.Features = request.Features.WithContext(requestHeader.Context);
                }

                lock (_mutex)
                {
                    // If shutdown, ignore the incoming request and continue receiving frames until the
                    // connection is closed.
                    if (!_shutdown)
                    {
                        _dispatches.Add(request);
                        request.CancelDispatchSource = new();
                        return request;
                    }
                }
            }
        }

        /// <inheritdoc/>
        public async Task<IncomingResponse> ReceiveResponseAsync(OutgoingRequest request, CancellationToken cancel)
        {
            if (request.IsOneway)
            {
                throw new InvalidOperationException("can't receive a response for a oneway request");
            }

            Ice1Request? requestFeature = request.Features.Get<Ice1Request>();
            if (requestFeature == null || requestFeature.ResponseCompletionSource == null)
            {
                throw new InvalidOperationException("unknown request");
            }

            // Wait for the response.
            Memory<byte> buffer;
            try
            {
                buffer = await requestFeature.ResponseCompletionSource.Task.WaitAsync(cancel).ConfigureAwait(false);
            }
            finally
            {
                lock (_mutex)
                {
                    if (_invocations.Remove(requestFeature.Id))
                    {
                        // If no more invocations or dispatches and shutting down, shutdown can complete.
                        if (_shutdown && _invocations.Count == 0 && _dispatches.Count == 0)
                        {
                            _dispatchesAndInvocationsCompleted.TrySetResult();
                        }
                    }
                }
            }

            // Decode the response.
            var decoder = new Ice11Decoder(buffer);

            ReplyStatus replyStatus = decoder.DecodeReplyStatus();
            ResultType resultType = replyStatus == ReplyStatus.OK ? ResultType.Success : ResultType.Failure;

            byte encodingMajor = 1;
            byte encodingMinor = 1;
            int? payloadSize = null;
            if (replyStatus <= ReplyStatus.UserException)
            {
                var responseHeader = new Ice1ResponseHeader(decoder);
                encodingMajor = responseHeader.PayloadEncodingMajor;
                encodingMinor = responseHeader.PayloadEncodingMinor;
                payloadSize = responseHeader.EncapsulationSize - 6;
            }

            var payloadEncoding = Encoding.FromMajorMinor(encodingMajor, encodingMinor);

            var features = new FeatureCollection();

            // For compatibility with ZeroC Ice
            if (request.Proxy is Proxy proxy &&
                replyStatus == ReplyStatus.ObjectNotExistException &&
                (proxy.Endpoint == null || proxy.Endpoint.Transport == TransportNames.Loc)) // "indirect" proxy
            {
                features.Set(RetryPolicy.OtherReplica);
            }

            Memory<byte> payload;
            if (payloadSize == null)
            {
                Debug.Assert(replyStatus > ReplyStatus.UserException);
                Debug.Assert(decoder.Pos == 1);

                // We need a new buffer

                payloadSize = buffer.Length; // includes reply status as first byte
                payload = new byte[4 + payloadSize.Value];
                buffer.CopyTo(payload[4..]);
            }
            else
            {
                // We overwrite the encapsulation header to write the payload size

                if (payloadEncoding == Encoding.Ice11 && resultType == ResultType.Failure)
                {
                    // We encode the reply status (UserException) after the payload size
                    Debug.Assert(replyStatus == ReplyStatus.UserException);
                    payload = buffer[(decoder.Pos - 5)..];
                    payload.Span[4] = (byte)ReplyStatus.UserException;
                    payloadSize += 1;
                }
                else
                {
                    payload = buffer[(decoder.Pos - 4)..]; // no reply status
                }

                if (payloadSize != payload.Length - 4)
                {
                    throw new InvalidDataException(@$"response payload size mismatch: expected {payloadSize
                        } bytes, read {payload.Length - 4} bytes");
                }
            }

            EncodePayloadSize(payloadSize.Value, payloadEncoding, payload.Span[0..4]);

            return new IncomingResponse(
                Protocol.Ice1, resultType,
                PipeReader.Create(new ReadOnlySequence<byte>(payload)),
                payloadEncoding)
            {
                Features = features,
            };
        }

        /// <inheritdoc/>
        public async Task SendRequestAsync(OutgoingRequest request, CancellationToken cancel)
        {
            if (request.StreamParamSender != null)
            {
                throw new NotSupportedException("stream parameters are not supported with ice1");
            }
            else if (request.Fields.Count > 0 || request.FieldsDefaults.Count > 0)
            {
                throw new NotSupportedException($"{nameof(Protocol.Ice1)} doesn't support fields");
            }
            else if (_isUdp && !request.IsOneway)
            {
                throw new InvalidOperationException("cannot send twoway request over UDP");
            }

            // Wait for sending of other frames to complete. The semaphore is used as an asynchronous queue to
            // serialize the sending of frames.
            await _sendSemaphore.EnterAsync(cancel).ConfigureAwait(false);

            // Assign the request ID for twoway invocations and keep track of the invocation for receiving the
            // response.
            int requestId = 0;
            if (!request.IsOneway)
            {
                try
                {
                    lock (_mutex)
                    {
                        if (_shutdown)
                        {
                            throw new ConnectionClosedException();
                        }
                        requestId = ++_nextRequestId;
                        _invocations[requestId] = request;
                        request.Features = request.Features.With(new Ice1Request(requestId, outgoing: true));
                    }
                }
                catch
                {
                    _sendSemaphore.Release();
                    throw;
                }
            }

            try
            {
                var bufferWriter = new BufferWriter();
                var encoder = new Ice11Encoder(bufferWriter);

                // Write the Ice1 request header.
                bufferWriter.WriteByteSpan(Ice1Definitions.FramePrologue);
                encoder.EncodeIce1FrameType(Ice1FrameType.Request);
                encoder.EncodeByte(0); // compression status
                BufferWriter.Position frameSizeStart = encoder.StartFixedLengthSize();

                encoder.EncodeInt(requestId);

                ReadOnlyMemory<ReadOnlyMemory<byte>> payload = request.Payload;

                (int payloadSize, byte encodingMajor, byte encodingMinor) =
                    DecodePayloadSize(ref payload, request.PayloadEncoding);

                var requestHeader = new Ice1RequestHeader(
                    IdentityAndFacet.FromPath(request.Path),
                    request.Operation,
                    request.IsIdempotent ? OperationMode.Idempotent : OperationMode.Normal,
                    request.Features.GetContext(),
                    encapsulationSize: payloadSize + 6,
                    encodingMajor,
                    encodingMinor);
                requestHeader.Encode(encoder);

                encoder.EncodeFixedLengthSize(bufferWriter.Size + payloadSize, frameSizeStart);

                // Add the payload to the buffer writer.
                bufferWriter.Add(payload);

                // Perform the sending. When an Ice1 frame is sent over a connection (such as a TCP
                // connection), we need to send the entire frame even when cancel gets canceled since the
                // recipient cannot read a partial frame and then keep going.
                ReadOnlyMemory<ReadOnlyMemory<byte>> buffers = bufferWriter.Finish();
                await _networkConnection.WriteAsync(buffers, CancellationToken.None).ConfigureAwait(false);

                // Mark the request as sent and, if it's a twoway request, keep track of it.
                request.IsSent = true;
            }
            catch (ObjectDisposedException exception)
            {
                // If the network connection has been disposed, we raise ConnectionLostException to ensure the
                // request is retried by the retry interceptor.
                // TODO: this is clunky but required for retries to work because the retry interceptor only retries
                // a request if the exception is a transport exception.
                throw new ConnectionLostException(exception);
            }
            finally
            {
                _sendSemaphore.Release();
            }
        }

        /// <inheritdoc/>
        public async Task SendResponseAsync(
            OutgoingResponse response,
            IncomingRequest request,
            CancellationToken cancel)
        {
            if (request.Features.GetRequestId() is not int requestId)
            {
                throw new InvalidOperationException("request ID feature is not set");
            }

            try
            {
                // Send the response if the request is not a one-way request.
                if (!request.IsOneway)
                {
                    // Wait for sending of other frames to complete. The semaphore is used as an asynchronous
                    // queue to serialize the sending of frames.
                    await _sendSemaphore.EnterAsync(cancel).ConfigureAwait(false);
                    try
                    {
                        var bufferWriter = new BufferWriter();
                        if (response.StreamParamSender != null)
                        {
                            throw new NotSupportedException("stream parameters are not supported with ice1");
                        }

                        var encoder = new Ice11Encoder(bufferWriter);

                        // Write the response header.
                        bufferWriter.WriteByteSpan(Ice1Definitions.FramePrologue);
                        encoder.EncodeIce1FrameType(Ice1FrameType.Reply);
                        encoder.EncodeByte(0); // compression status
                        BufferWriter.Position frameSizeStart = encoder.StartFixedLengthSize();

                        encoder.EncodeInt(requestId);

                        ReadOnlyMemory<ReadOnlyMemory<byte>> payload = response.Payload;

                        (int payloadSize, byte encodingMajor, byte encodingMinor) =
                            DecodePayloadSize(ref payload, response.PayloadEncoding);

                        ReplyStatus replyStatus = ReplyStatus.OK;

                        if (response.ResultType == ResultType.Failure)
                        {
                            if (response.PayloadEncoding == Encoding.Ice11)
                            {
                                // extract reply status from 1.1-encoded payload
                                replyStatus = (ReplyStatus)payload.Span[0].Span[0];
                                payload = SliceBuffers(payload, 1);
                                payloadSize -= 1;
                            }
                            else
                            {
                                replyStatus = ReplyStatus.UserException;
                            }
                        }

                        encoder.EncodeReplyStatus(replyStatus);
                        if (replyStatus <= ReplyStatus.UserException)
                        {
                            var responseHeader = new Ice1ResponseHeader(encapsulationSize: payloadSize + 6,
                                                                        encodingMajor,
                                                                        encodingMinor);
                            responseHeader.Encode(encoder);
                        }

                        encoder.EncodeFixedLengthSize(bufferWriter.Size + payloadSize, frameSizeStart);

                        // Add the payload to the buffer writer.
                        bufferWriter.Add(payload);

                        // Send the response frame.
                        ReadOnlyMemory<ReadOnlyMemory<byte>> buffers = bufferWriter.Finish();
                        await _networkConnection.WriteAsync(buffers, CancellationToken.None).ConfigureAwait(false);
                    }
                    finally
                    {
                        _sendSemaphore.Release();
                    }
                }
            }
            finally
            {
                lock (_mutex)
                {
                    // Dispatch is done, remove the cancellation token source for the dispatch.
                    if (_dispatches.Remove(request))
                    {
                        request.CancelDispatchSource!.Dispose();

                        // If no more invocations or dispatches and shutting down, shutdown can complete.
                        if (_shutdown && _invocations.Count == 0 && _dispatches.Count == 0)
                        {
                            _dispatchesAndInvocationsCompleted.TrySetResult();
                        }
                    }
                }
            }
        }

        public async Task ShutdownAsync(string message, CancellationToken cancel)
        {
            var exception = new ConnectionClosedException(message);
            if (_isUdp)
            {
                lock (_mutex)
                {
                    _shutdown = true;
                    _sendSemaphore.Complete(exception);
                }
            }
            else
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
                        _shutdown = true;
                        if (_dispatches.Count == 0 && _invocations.Count == 0)
                        {
                            _dispatchesAndInvocationsCompleted.TrySetResult();
                        }
                    }
                }

                if (!alreadyShuttingDown)
                {
                    // Cancel pending invocations immediately. Wait for dispatches to complete however.
                    CancelInvocations(new OperationCanceledException(message));
                }

                try
                {
                    // Wait for dispatches to complete.
                    await _dispatchesAndInvocationsCompleted.Task.WaitAsync(cancel).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Try to speed up dispatch completion.
                    CancelDispatches();

                    // Wait again for the dispatches to complete.
                    await _dispatchesAndInvocationsCompleted.Task.ConfigureAwait(false);
                }

                // Cancel any pending requests waiting for sending.
                _sendSemaphore.Complete(exception);

                // Send the CloseConnection frame once all the dispatches are done.
                await _networkConnection.WriteAsync(
                    Ice1Definitions.CloseConnectionFrame,
                    CancellationToken.None).ConfigureAwait(false);

                // When the peer receives the CloseConnection frame, the peer closes the connection. We wait for the
                // connection closure here. We can't just return and close the underlying transport since this could
                // abort the receive of the dispatch responses and close connection frame by the peer.
                await _pendingClose.Task.ConfigureAwait(false);
            }
        }

        internal Ice1ProtocolConnection(
            ISimpleNetworkConnection simpleNetworkConnection,
            int incomingFrameMaxSize,
            bool isUdp)
        {
            _isUdp = isUdp;
            _incomingFrameMaxSize = incomingFrameMaxSize;
            _networkConnection = simpleNetworkConnection;
        }

        internal async Task InitializeAsync(bool isServer, CancellationToken cancel)
        {
            if (!_isUdp)
            {
                if (isServer)
                {
                    await _networkConnection.WriteAsync(
                        Ice1Definitions.ValidateConnectionFrame,
                        cancel).ConfigureAwait(false);
                }
                else
                {
                    Memory<byte> buffer = new byte[Ice1Definitions.HeaderSize];
                    await ReceiveUntilFullAsync(buffer, cancel).ConfigureAwait(false);

                    // Check the header
                    Ice1Definitions.CheckHeader(buffer.Span[0..Ice1Definitions.HeaderSize]);
                    int frameSize = IceDecoder.DecodeInt(buffer.AsReadOnlySpan().Slice(10, 4));
                    if (frameSize != Ice1Definitions.HeaderSize)
                    {
                        throw new InvalidDataException($"received ice1 frame with only '{frameSize}' bytes");
                    }
                    if ((Ice1FrameType)buffer.Span[8] != Ice1FrameType.ValidateConnection)
                    {
                        throw new InvalidDataException(@$"expected '{nameof(Ice1FrameType.ValidateConnection)
                            }' frame but received frame type '{(Ice1FrameType)buffer.Span[8]}'");
                    }
                }
            }
        }

        /// <summary>Decodes the first byte of the payload to get its size.</summary>
        private static (int PayloadSize, byte EncodingMajor, byte EncodingMinor) DecodePayloadSize(
            ref ReadOnlyMemory<ReadOnlyMemory<byte>> payload,
            Encoding payloadEncoding)
        {
            int payloadSize;
            int payloadSizeLength;
            byte encodingMajor;
            byte encodingMinor;

            if (payloadEncoding == Encoding.Ice11)
            {
                encodingMajor = 1;
                encodingMinor = 1;
                payloadSizeLength = 4;
                payloadSize = IceDecoder.DecodeInt(payload.Span[0].Span);
            }
            else if (payloadEncoding == Encoding.Ice20)
            {
                encodingMajor = 2;
                encodingMinor = 0;
                (payloadSize, payloadSizeLength) = Ice20Decoder.DecodeSize(payload.Span[0].Span);
            }
            else
            {
                throw new NotSupportedException("an ice1 payload must be encoded with Slice 1.1 or Slice 2.0");
            }

            payload = SliceBuffers(payload, payloadSizeLength);
            return (payloadSize, encodingMajor, encodingMinor);
        }

        // Helper method that removes a few bytes from the first buffer. The implementation is simple and limited.
        private static ReadOnlyMemory<ReadOnlyMemory<byte>> SliceBuffers(
            ReadOnlyMemory<ReadOnlyMemory<byte>> buffers,
            int start)
        {
            if (buffers.Length > 1 || buffers.Span[0].Length > start)
            {
                var result = new ReadOnlyMemory<byte>[buffers.Length];
                result[0] = buffers.Span[0][start..];
                Debug.Assert(result[0].Length > 0);
                for (int i = 1; i < buffers.Length; ++i)
                {
                    result[i] = buffers.Span[i];
                }
                return result;
            }
            else
            {
                Debug.Assert(buffers.Length == 1 && buffers.Span[0].Length == start);
                return ReadOnlyMemory<ReadOnlyMemory<byte>>.Empty;
            }
        }

        /// <summary>Encodes a payload size into a buffer with the specified encoding.</summary>
        private static void EncodePayloadSize(int payloadSize, Encoding payloadEncoding, Span<byte> buffer)
        {
            Debug.Assert(buffer.Length == 4);

            if (payloadEncoding == Encoding.Ice11)
            {
                IceEncoder.EncodeInt(payloadSize, buffer);
            }
            else if (payloadEncoding == Encoding.Ice20)
            {
                Ice20Encoder.EncodeFixedLengthSize(payloadSize, buffer);
            }
            else
            {
                throw new NotSupportedException("an ice1 payload must be encoded with Slice 1.1 or Slice 2.0");
            }
        }

        private void CancelDispatches()
        {
            IEnumerable<IncomingRequest> dispatches;
            lock (_mutex)
            {
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
        }

        private void CancelInvocations(Exception exception)
        {
            IEnumerable<OutgoingRequest> invocations;
            lock (_mutex)
            {
                invocations = _invocations.Values.ToArray();
            }

            foreach (OutgoingRequest request in invocations)
            {
                request.Features.Get<Ice1Request>()!.ResponseCompletionSource!.TrySetException(exception);
            }
        }

        private async ValueTask<(int, Memory<byte>)> ReceiveFrameAsync()
        {
            // Reads are not cancellable. This method returns once a frame is read or when the connection is disposed.
            CancellationToken cancel = CancellationToken.None;

            while (true)
            {
                // Receive the Ice1 frame header.
                Memory<byte> buffer;
                if (_isUdp)
                {
                    buffer = new byte[_incomingFrameMaxSize];
                    int received = await _networkConnection.ReadAsync(buffer, cancel).ConfigureAwait(false);
                    if (received < Ice1Definitions.HeaderSize)
                    {
                        // TODO: implement protocol logging with decorators
                        //_logger.LogReceivedInvalidDatagram(received);
                        continue; // while
                    }
                    buffer = buffer[0..received];
                }
                else
                {
                    // TODO: rent buffer from memory pool
                    buffer = new byte[256];
                    await ReceiveUntilFullAsync(buffer[0..Ice1Definitions.HeaderSize], cancel).ConfigureAwait(false);
                }

                // Check the header
                Ice1Definitions.CheckHeader(buffer.Span[0..Ice1Definitions.HeaderSize]);
                int frameSize = IceDecoder.DecodeInt(buffer.AsReadOnlySpan().Slice(10, 4));
                if (_isUdp && frameSize != buffer.Length)
                {
                    // TODO: implement protocol logging with decorators
                    // _logger.LogReceivedInvalidDatagram(frameSize);
                    continue; // while
                }
                else if (frameSize > _incomingFrameMaxSize)
                {
                    if (_isUdp)
                    {
                        // TODO: implement protocol logging with decorators
                        // _logger.LogDatagramSizeExceededIncomingFrameMaxSize(frameSize);
                        continue;
                    }
                    else
                    {
                        throw new InvalidDataException(
                            $"frame with {frameSize} bytes exceeds IncomingFrameMaxSize connection option value");
                    }
                }

                // The magic and version fields have already been checked.
                var frameType = (Ice1FrameType)buffer.Span[8];
                byte compressionStatus = buffer.Span[9];
                if (compressionStatus == 2)
                {
                    throw new NotSupportedException("cannot decompress ice1 frame");
                }

                // Read the remainder of the frame if needed.
                if (_isUdp)
                {
                    Debug.Assert(frameSize == buffer.Length);
                    buffer = buffer[Ice1Definitions.HeaderSize..];
                }
                else if (frameSize == Ice1Definitions.HeaderSize)
                {
                    buffer = Memory<byte>.Empty;
                }
                else
                {
                    int remainingSize = frameSize - Ice1Definitions.HeaderSize;
                    // TODO: rent buffer from memory pool
                    buffer = buffer.Length < remainingSize ? new byte[remainingSize] : buffer[0..remainingSize];
                    await ReceiveUntilFullAsync(buffer, cancel).ConfigureAwait(false);
                }

                switch (frameType)
                {
                    case Ice1FrameType.CloseConnection:
                    {
                        if (buffer.Length > 0)
                        {
                            throw new InvalidDataException(
                                $"unexpected data for {nameof(Ice1FrameType.CloseConnection)}");
                        }
                        if (_isUdp)
                        {
                            throw new InvalidDataException(
                                $"unexpected {nameof(Ice1FrameType.CloseConnection)} frame for udp connection");
                        }

                        lock (_mutex)
                        {
                            // If local shutdown is in progress, shutdown from peer prevails. The local shutdown
                            // will return once the connection disposes this protocol connection.
                            _shutdown = true;
                        }

                        // Raise the peer shutdown initiated event.
                        try
                        {
                            PeerShutdownInitiated?.Invoke();
                        }
                        catch (Exception ex)
                        {
                            Debug.Assert(false, $"{nameof(PeerShutdownInitiated)} raised unexpected exception\n{ex}");
                        }

                        var exception = new ConnectionClosedException("connection shutdown by peer");

                        // The peer cancels its invocations on shutdown so we can cancel the dispatches.
                        CancelDispatches();

                        // The peer didn't dispatch invocations which are still in progress, these invocations can
                        // therefore be retried (completing the invocation here ensures that the invocations won't get
                        // ConnectionLostException from Dispose).
                        CancelInvocations(exception);

                        // New requests will complete with ConnectionClosedException.
                        _sendSemaphore.Complete(exception);

                        throw exception;
                    }

                    case Ice1FrameType.Request:
                    {
                        int requestId = IceDecoder.DecodeInt(buffer.Span[0..4]);
                        return (requestId, buffer[4..]);
                    }

                    case Ice1FrameType.RequestBatch:
                    {
                        int invokeNum = IceDecoder.DecodeInt(buffer.Span[0..4]);
                        // TODO: implement protocol logging with decorators
                        // _logger.LogReceivedIce1RequestBatchFrame(invokeNum);

                        if (invokeNum < 0)
                        {
                            throw new InvalidDataException(
                                $"received ice1 RequestBatchMessage with {invokeNum} batch requests");
                        }
                        break; // Batch requests are ignored because not supported
                    }

                    case Ice1FrameType.Reply:
                    {
                        int requestId = IceDecoder.DecodeInt(buffer.Span[0..4]);
                        lock (_mutex)
                        {
                            if (_invocations.TryGetValue(requestId, out OutgoingRequest? request))
                            {
                                request.Features.Get<Ice1Request>()!.ResponseCompletionSource!.SetResult(buffer[4..]);
                            }
                            else if (!_shutdown)
                            {
                                throw new InvalidDataException("received ice1 Reply for unknown invocation");
                            }
                        }
                        break;
                    }

                    case Ice1FrameType.ValidateConnection:
                    {
                        // Notify the control stream of the reception of a Ping frame.
                        if (buffer.Length > 0)
                        {
                            throw new InvalidDataException(
                                $"unexpected data for {nameof(Ice1FrameType.ValidateConnection)}");
                        }
                        // TODO: implement protocol logging with decorators
                        // _logger.LogReceivedIce1ValidateConnectionFrame();
                        break;
                    }

                    default:
                    {
                        throw new InvalidDataException($"received ice1 frame with unknown frame type '{frameType}'");
                    }
                }
            }
        }

        private async ValueTask ReceiveUntilFullAsync(Memory<byte> buffer, CancellationToken cancel)
        {
            int offset = 0;
            while (offset != buffer.Length)
            {
                offset += await _networkConnection.ReadAsync(buffer[offset..], cancel).ConfigureAwait(false);
            }
        }
    }
}
