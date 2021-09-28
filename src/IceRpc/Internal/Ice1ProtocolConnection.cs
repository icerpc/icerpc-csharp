// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Slice;
using IceRpc.Slice.Internal;
using IceRpc.Transports;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace IceRpc.Internal
{
    internal sealed class Ice1ProtocolConnection : IProtocolConnection
    {
        /// <inheritdoc/>
        public bool HasDispatchInProgress => _dispatchCancellationTokenSources.Count > 0;
        /// <inheritdoc/>
        public bool HasInvocationsInProgress => _pendingIncomingResponses.Count > 0;

        // TODO: XXX, add back configuration to limit the number of concurrent dispatch.
        // private readonly AsyncSemaphore? _bidirectionalStreamSemaphore;
        private readonly TaskCompletionSource _cancelShutdown =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _dispatchAndInvocationsCompleted = new();
        private readonly Dictionary<int, CancellationTokenSource> _dispatchCancellationTokenSources = new();
        private readonly int _incomingFrameMaxSize;
        private readonly bool _isServer;
        private readonly bool _isDatagram;
        private readonly int _datagramMaxReceiveSize;
        private readonly ILogger _logger;
        private readonly object _mutex = new();
        private readonly ISingleStreamConnection _stream;
        private int _nextRequestId;
        private readonly TaskCompletionSource _pendingCloseConnection =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _pendingClose = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Dictionary<int, TaskCompletionSource<ReadOnlyMemory<byte>>> _pendingIncomingResponses = new();
        private readonly AsyncSemaphore _sendSemaphore = new(1);
        // TODO: XXX, add back configuration to limit the number of concurrent dispatch.
        // private readonly AsyncSemaphore? _unidirectionalStreamSemaphore;
        private bool _shutdown;

        /// <summary>Creates a multi-stream protocol connection.</summary>
        public Ice1ProtocolConnection(
            ISingleStreamConnection singleStreamConnection,
            int incomingFrameMaxSize,
            bool isServer,
            int? datagramMaxReceiveSize,
            ILogger logger)
        {
            _stream = singleStreamConnection;
            _incomingFrameMaxSize = incomingFrameMaxSize;
            _isServer = isServer;
            _isDatagram = datagramMaxReceiveSize != null;
            _datagramMaxReceiveSize = datagramMaxReceiveSize ?? 0;
            _logger = logger;
        }

        /// <inheritdoc/>
        public async Task InitializeAsync(CancellationToken cancel)
        {
            if (!_isDatagram)
            {
                if (_isServer)
                {
                    await _stream.SendAsync(Ice1Definitions.ValidateConnectionFrame, cancel).ConfigureAwait(false);
                }
                else
                {
                    Memory<byte> buffer = new byte[Ice1Definitions.HeaderSize];
                    await ReceiveUntilFullAsync(buffer, cancel).ConfigureAwait(false);

                    // Check the header
                    Ice1Definitions.CheckHeader(buffer.Span.Slice(0, Ice1Definitions.HeaderSize));
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

        /// <inheritdoc/>
        public void CancelShutdown() =>
            // Notify the task completion source that shutdown was canceled. PerformShutdownAsync will
            // cancel the dispatch. We can't cancel the dispatch until ShutdownAsync is called.
            _cancelShutdown.TrySetResult();

        /// <inheritdoc/>
        public void Dispose()
        {
            lock (_mutex)
            {
                _cancelShutdown.TrySetResult();
                _pendingClose.TrySetResult();
                var exception = new ConnectionLostException();
                _sendSemaphore.Complete(exception);
                _pendingCloseConnection.TrySetException(exception);
                foreach (TaskCompletionSource<ReadOnlyMemory<byte>> source in _pendingIncomingResponses.Values)
                {
                    source.SetException(exception);
                }
                _pendingIncomingResponses.Clear();
                foreach (CancellationTokenSource cancellationTokenSource in _dispatchCancellationTokenSources.Values)
                {
                    cancellationTokenSource.Dispose();
                }
                _dispatchCancellationTokenSources.Clear();
            }
        }

        /// <inheritdoc/>
        public async Task PingAsync(CancellationToken cancel)
        {
            await _sendSemaphore.EnterAsync(cancel).ConfigureAwait(false);
            try
            {
                await _stream.SendAsync(Ice1Definitions.ValidateConnectionFrame, cancel).ConfigureAwait(false);
            }
            finally
            {
                _sendSemaphore.Release();
            }

            _logger.LogSentIce1ValidateConnectionFrame();
        }

        /// <inheritdoc/>
        public async Task<IncomingRequest> ReceiveRequestAsync(CancellationToken cancel)
        {
            while (true)
            {
                int requestId;
                ReadOnlyMemory<byte> buffer;
                try
                {
                    (requestId, buffer) = await ReceiveFrameAsync(cancel).ConfigureAwait(false);
                }
                catch
                {
                    // TODO: XXX Is this really needed??
                    lock (_mutex)
                    {
                        if (_shutdown)
                        {
                            _pendingClose.TrySetResult();
                            throw new ConnectionClosedException();
                        }
                    }
                    throw;
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

                ReadOnlyMemory<byte> payload = buffer[decoder.Pos..];

                // The payload size is the encapsulation size less the 6 bytes of the encapsulation header.
                int payloadSize = requestHeader.EncapsulationSize - 6;
                if (payloadSize != payload.Length)
                {
                    throw new InvalidDataException(
                        $"request payload size mismatch: expected {payloadSize} bytes, read {payload.Length} bytes");
                }

                var request = new IncomingRequest(
                    Protocol.Ice1,
                    path: requestHeader.IdentityAndFacet.ToPath(),
                    operation: requestHeader.Operation)
                {
                    IsIdempotent = requestHeader.OperationMode != OperationMode.Normal,
                    IsOneway = requestId == 0,
                    PayloadEncoding =
                        Encoding.FromMajorMinor(requestHeader.PayloadEncodingMajor, requestHeader.PayloadEncodingMinor),
                    Deadline = DateTime.MaxValue,
                    Payload = payload,
                    CancelDispatchSource = new()
                };
                request.Features = request.Features.WithRequestId(requestId);
                if (requestHeader.Context.Count > 0)
                {
                    request.Features = request.Features.WithContext(requestHeader.Context);
                }

                lock (_mutex)
                {
                    // If shutdown, ignore the incoming request and continue receiving frames until the connection
                    // is closed.
                    if (_shutdown)
                    {
                        request.CancelDispatchSource.Dispose();
                    }
                    else
                    {
                        // TODO: XXX fix this, it's wrong for oneway requests which use 0 for the requestId.
                        _dispatchCancellationTokenSources[requestId] = request.CancelDispatchSource;
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
                throw new InvalidOperationException("can't receive a response for a one-way request");
            }
            if (request.Features.GetRequestId() is not int requestId)
            {
                throw new InvalidOperationException("request ID feature is not set");
            }

            // Wait for the response.
            ReadOnlyMemory<byte> buffer;
            try
            {
                Task<ReadOnlyMemory<byte>> responseTask;
                lock (_mutex)
                {
                    if (_shutdown)
                    {
                        throw new ConnectionClosedException();
                    }
                    else if (_pendingIncomingResponses.TryGetValue(
                        requestId,
                        out TaskCompletionSource<ReadOnlyMemory<byte>>? source))
                    {
                        responseTask = source.Task;
                    }
                    else
                    {
                        throw new InvalidOperationException($"unknown request with requestId {requestId}");
                    }
                }
                buffer = await responseTask.WaitAsync(cancel).ConfigureAwait(false);
            }
            finally
            {
                lock (_mutex)
                {
                    if (_pendingIncomingResponses.Remove(requestId))
                    {
                        // If no more invocations or dispatch and shutting down, shutdown can complete.
                        if (_shutdown &&
                            _pendingIncomingResponses.Count == 0 &&
                            _dispatchCancellationTokenSources.Count == 0)
                        {
                            _dispatchAndInvocationsCompleted.SetResult();
                        }
                    }
                }
            }

            // Decode the response.
            var decoder = new Ice11Decoder(buffer);

            ReplyStatus replyStatus = decoder.DecodeReplyStatus();

            var features = new FeatureCollection();
            features.Set(replyStatus);

            Encoding payloadEncoding;
            int? payloadSize = null;
            if (replyStatus <= ReplyStatus.UserException)
            {
                var responseHeader = new Ice1ResponseHeader(decoder);
                payloadEncoding = Encoding.FromMajorMinor(responseHeader.PayloadEncodingMajor,
                                                          responseHeader.PayloadEncodingMinor);
                payloadSize = responseHeader.EncapsulationSize - 6;
            }
            else
            {
                payloadEncoding = Encoding.Ice11;
            }

            // For compatibility with ZeroC Ice
            if (request.Proxy is Proxy proxy &&
                replyStatus == ReplyStatus.ObjectNotExistException &&
                (proxy.Endpoint == null || proxy.Endpoint.Transport == TransportNames.Loc)) // "indirect" proxy
            {
                features.Set(RetryPolicy.OtherReplica);
            }

            ReadOnlyMemory<byte> payload = buffer[decoder.Pos..];
            if (payloadSize != null && payloadSize != payload.Length)
            {
                throw new InvalidDataException(
                    @$"response payload size mismatch: expected {payloadSize} bytes, read {payload.Length} bytes");
            }

            return new IncomingResponse(
                Protocol.Ice1,
                replyStatus == ReplyStatus.OK ? ResultType.Success : ResultType.Failure)
            {
                Features = features,
                PayloadEncoding = payloadEncoding,
                Payload = payload
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
            else if (_isDatagram && !request.IsOneway)
            {
                throw new InvalidOperationException("cannot send twoway request over datagram connection");
            }

            // Wait for sending of other frames to complete. The semaphore is used as an asynchronous queue to
            // serialize the sending of frames.
            await _sendSemaphore.EnterAsync(cancel).ConfigureAwait(false);

            int requestId = 0;
            if (!request.IsOneway)
            {
                lock (_mutex)
                {
                    if (_shutdown)
                    {
                        throw new ConnectionClosedException();
                    }
                    requestId = ++_nextRequestId;
                    _pendingIncomingResponses[requestId] = new(TaskCreationOptions.RunContinuationsAsynchronously);
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

                request.Features = request.Features.WithRequestId(requestId);
                encoder.EncodeInt(requestId);

                (byte encodingMajor, byte encodingMinor) = request.PayloadEncoding.ToMajorMinor();

                var requestHeader = new Ice1RequestHeader(
                    IdentityAndFacet.FromPath(request.Path),
                    request.Operation,
                    request.IsIdempotent ? OperationMode.Idempotent : OperationMode.Normal,
                    request.Features.GetContext(),
                    encapsulationSize: request.PayloadSize + 6,
                    encodingMajor,
                    encodingMinor);
                requestHeader.Encode(encoder);

                encoder.EncodeFixedLengthSize(bufferWriter.Size + request.PayloadSize, frameSizeStart);

                // Add the payload to the buffer writer.
                bufferWriter.Add(request.Payload);

                // Perform the sending. When an Ice1 frame is sent over a connection (such as a TCP
                // connection), we need to send the entire frame even when cancel gets canceled since the
                // recipient cannot read a partial frame and then keep going.
                ReadOnlyMemory<ReadOnlyMemory<byte>> buffers = bufferWriter.Finish();
                await _stream.SendAsync(buffers, CancellationToken.None).ConfigureAwait(false);

                // Mark the request as sent and, if it's a twoway request, keep track of it.
                request.IsSent = true;
            }
            catch
            {
                lock (_mutex)
                {
                    if (_pendingIncomingResponses.Remove(requestId))
                    {
                        // If no more invocations or dispatch and shutting down, shutdown can complete.
                        if (_shutdown &&
                            _pendingIncomingResponses.Count == 0 &&
                            _dispatchCancellationTokenSources.Count == 0)
                        {
                            _dispatchAndInvocationsCompleted.SetResult();
                        }
                    }
                }
                throw;
            }
            finally
            {
                _sendSemaphore.Release();
            }
        }

        /// <inheritdoc/>
        public async Task SendResponseAsync(
            IncomingRequest request,
            OutgoingResponse response,
            CancellationToken cancel)
        {
            if (request.Features.GetRequestId() is not int requestId)
            {
                throw new InvalidOperationException("request ID feature is not set");
            }

            // Send the response if the request is not a one-way request.
            try
            {
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

                        ReplyStatus replyStatus = response.Features.Get<ReplyStatus>();
                        encoder.EncodeReplyStatus(replyStatus);
                        if (replyStatus <= ReplyStatus.UserException)
                        {
                            (byte encodingMajor, byte encodingMinor) = response.PayloadEncoding.ToMajorMinor();

                            var responseHeader = new Ice1ResponseHeader(encapsulationSize: response.PayloadSize + 6,
                                                                        encodingMajor,
                                                                        encodingMinor);
                            responseHeader.Encode(encoder);
                        }

                        encoder.EncodeFixedLengthSize(bufferWriter.Size + response.PayloadSize, frameSizeStart);

                        // Add the payload to the buffer writer.
                        bufferWriter.Add(response.Payload);

                        // Send the response frame.
                        ReadOnlyMemory<ReadOnlyMemory<byte>> buffers = bufferWriter.Finish();
                        await _stream.SendAsync(buffers, CancellationToken.None).ConfigureAwait(false);
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
                    if (_dispatchCancellationTokenSources.Remove(requestId))
                    {
                        request.CancelDispatchSource!.Dispose();

                        // If no more invocations or dispatch and shutting down, shutdown can complete.
                        if (_shutdown &&
                            _pendingIncomingResponses.Count == 0 &&
                            _dispatchCancellationTokenSources.Count == 0)
                        {
                            _dispatchAndInvocationsCompleted.SetResult();
                        }
                    }
                }
            }
        }

        /// <inheritdoc/>
        public async Task ShutdownAsync(bool shutdownByPeer, string message, CancellationToken cancel)
        {
            var exception = new ConnectionClosedException(message);
            if (_isDatagram)
            {
                lock (_mutex)
                {
                    _shutdown = true;
                    _sendSemaphore.Complete(exception);
                }
            }
            else
            {
                lock (_mutex)
                {
                    _shutdown = true;

                    // If shutdown locally, we raise OperationCanceledException for pending invocations. The
                    // invocation won't be retried, otherwise we raise the shutdown close exception.
                    Exception closeException = shutdownByPeer ? exception : new OperationCanceledException(message);
                    foreach (TaskCompletionSource<ReadOnlyMemory<byte>> source in _pendingIncomingResponses.Values)
                    {
                        source.TrySetException(closeException);
                    }
                    _pendingIncomingResponses.Clear();

                    if (_dispatchCancellationTokenSources.Count == 0)
                    {
                        _dispatchAndInvocationsCompleted.TrySetResult();
                    }
                }

                if (shutdownByPeer)
                {
                    // Peer sent CloseConnection frame, we can cancel remaining dispatch since the peer is no
                    // longer interested in the dispatch responses.
                    CancelDispatch();
                }
                else
                {
                    _ = CancelDispatchIfShutdownCanceledAsync();

                    // Wait for dispatch to complete.
                    await _dispatchAndInvocationsCompleted.Task.WaitAsync(cancel).ConfigureAwait(false);
                }

                _sendSemaphore.Complete(exception);

                if (!shutdownByPeer)
                {
                    // Send the CloseConnection frame once all the dispatch are done.
                    await _stream.SendAsync(Ice1Definitions.CloseConnectionFrame, cancel).ConfigureAwait(false);

                    // Wait for the peer to close the connection. When the peer receives the CloseConnectionFrame
                    // the peer closes the connection. This will cause ReceiveRequestAsync to throw and the
                    // connection will call Dispose to terminate the protocol connection.
                    await _pendingClose.Task.WaitAsync(cancel).ConfigureAwait(false);
                }
            }

            async Task CancelDispatchIfShutdownCanceledAsync()
            {
                // Wait for the shutdown cancellation.
                await _cancelShutdown.Task.ConfigureAwait(false);

                // Cancel dispatch if shutdown is canceled.
                CancelDispatch();
            }
        }

        public async Task<string> WaitForShutdownAsync(CancellationToken cancel)
        {
            await _pendingCloseConnection.Task.WaitAsync(cancel).ConfigureAwait(false);
            return "connection graceful shutdown";
        }

        private void CancelDispatch()
        {
            lock (_mutex)
            {
                Debug.Assert(_shutdown);
                foreach (CancellationTokenSource source in _dispatchCancellationTokenSources.Values)
                {
                    source.Cancel();
                }
            }
        }

        private async ValueTask<(int, ReadOnlyMemory<byte>)> ReceiveFrameAsync(CancellationToken cancel)
        {
            while (true)
            {
                // Receive the Ice1 frame header.
                Memory<byte> buffer;
                if (_isDatagram)
                {
                    buffer = new byte[_datagramMaxReceiveSize];
                    int received = await _stream.ReceiveAsync(buffer, cancel).ConfigureAwait(false);
                    if (received < Ice1Definitions.HeaderSize)
                    {
                        _logger.LogReceivedInvalidDatagram(received);
                        continue; // while
                    }

                    buffer = buffer[0..received];
                }
                else
                {
                    buffer = new byte[256];
                    await ReceiveUntilFullAsync(buffer[0..Ice1Definitions.HeaderSize], cancel).ConfigureAwait(false);
                }

                // Check the header
                Ice1Definitions.CheckHeader(buffer.Span.Slice(0, Ice1Definitions.HeaderSize));
                int frameSize = IceDecoder.DecodeInt(buffer.AsReadOnlySpan().Slice(10, 4));
                if (frameSize < Ice1Definitions.HeaderSize)
                {
                    if (_isDatagram)
                    {
                        _logger.LogReceivedInvalidDatagram(frameSize);
                    }
                    else
                    {
                        throw new InvalidDataException(
                            @$"received ice1 frame with invalid size, expected '{Ice1Definitions.HeaderSize
                                }' but received '{frameSize}' bytes");
                    }
                    continue; // while
                }
                if (frameSize > _incomingFrameMaxSize || (_isDatagram && frameSize > _datagramMaxReceiveSize))
                {
                    if (_isDatagram)
                    {
                        _logger.LogDatagramSizeExceededIncomingFrameMaxSize(frameSize);
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
                int remainingSize = frameSize - Ice1Definitions.HeaderSize;
                if (_isDatagram)
                {
                    Debug.Assert(remainingSize < buffer.Length);
                    buffer = buffer[0..remainingSize];
                }
                else if (remainingSize > 0)
                {
                    if (buffer.Length < remainingSize)
                    {
                        buffer = new byte[remainingSize];
                    }
                    else
                    {
                        buffer = buffer[0..remainingSize];
                    }
                    await ReceiveUntilFullAsync(buffer, cancel).ConfigureAwait(false);
                }
                else
                {
                    buffer = Memory<byte>.Empty;
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
                        _pendingCloseConnection.TrySetResult();
                        break;
                    }

                    case Ice1FrameType.Request:
                    {
                        int requestId = IceDecoder.DecodeInt(buffer.Span.Slice(0, 4));
                        return (requestId, buffer[4..]);
                    }

                    case Ice1FrameType.RequestBatch:
                    {
                        int invokeNum = IceDecoder.DecodeInt(buffer.Span.Slice(0, 4));
                        _logger.LogReceivedIce1RequestBatchFrame(invokeNum);

                        if (invokeNum < 0)
                        {
                            throw new InvalidDataException(
                                $"received ice1 RequestBatchMessage with {invokeNum} batch requests");
                        }
                        break; // Batch requests are not ignored because not supported
                    }

                    case Ice1FrameType.Reply:
                    {
                        int requestId = IceDecoder.DecodeInt(buffer.Span.Slice(0, 4));
                        lock (_mutex)
                        {
                            if (!_shutdown &&
                                _pendingIncomingResponses.TryGetValue(
                                requestId,
                                out TaskCompletionSource<ReadOnlyMemory<byte>>? source))
                            {
                                source.SetResult(buffer[4..]);
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
                        _logger.LogReceivedIce1ValidateConnectionFrame();
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
                offset += await _stream.ReceiveAsync(buffer[offset..], cancel).ConfigureAwait(false);
            }
        }
    }
}
