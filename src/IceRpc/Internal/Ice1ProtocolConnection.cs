// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Slice;
using IceRpc.Slice.Internal;
using IceRpc.Transports;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace IceRpc.Internal
{
    internal sealed class Ice1ProtocolConnection : IProtocolConnection
    {
        /// <inheritdoc/>
        public bool HasDispatchInProgress => !_dispatchCancellationTokenSources.IsEmpty;
        /// <inheritdoc/>
        public bool HasInvocationsInProgress => !_pendingIncomingResponses.IsEmpty;

        /// <inheritdoc/>
        public TimeSpan IdleTimeout { get; private set; }

        /// <inheritdoc/>
        public TimeSpan LastActivity { get; private set; }

//        private readonly AsyncSemaphore? _bidirectionalStreamSemaphore;
        private TaskCompletionSource? _dispatchEmptyTaskCompletionSource;
        private readonly int _incomingFrameMaxSize;
        private readonly bool _isServer;
        private readonly ILogger _logger;
        private int _nextRequestId;
        private readonly ConcurrentDictionary<int, TaskCompletionSource<ReadOnlyMemory<byte>>> _pendingIncomingResponses = new();
        private readonly ConcurrentDictionary<int, CancellationTokenSource> _dispatchCancellationTokenSources = new();
        private readonly TaskCompletionSource _pendingCloseConnection = new();
        private readonly Action? _pingReceived;
        private readonly AsyncSemaphore _sendSemaphore = new(1);
//        private readonly AsyncSemaphore? _unidirectionalStreamSemaphore;
        private volatile bool _shutdown;
        private readonly NetworkSocket _socket;
        private INetworkConnection _networkConnection;

        /// <summary>Creates a multi-stream protocol connection.</summary>
        public Ice1ProtocolConnection(
            SocketConnection networkConnection,
            TimeSpan idleTimeout,
            int incomingFrameMaxSize,
            bool isServer,
            Action? pingReceived,
            ILoggerFactory loggerFactory)
        {
            _networkConnection = networkConnection;
            IdleTimeout = idleTimeout;
            _incomingFrameMaxSize = incomingFrameMaxSize;
            _isServer = isServer;
            _pingReceived = pingReceived;
            _logger = loggerFactory.CreateLogger("IceRpc.Protocol");
            _socket = networkConnection.NetworkSocket;
        }

        /// <inheritdoc/>
        public async Task InitializeAsync(CancellationToken cancel)
        {
            await _networkConnection.ConnectAsync(cancel).ConfigureAwait(false);

            if (!_networkConnection.IsDatagram)
            {
                if (_isServer)
                {
                    await SendControlFrameAsync(Ice1Definitions.ValidateConnectionFrame, cancel).ConfigureAwait(false);
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
        public void CancelShutdown() => CancelDispatch();

        /// <inheritdoc/>
        public void Dispose()
        {
            _networkConnection.Dispose();
            foreach (CancellationTokenSource cancellationTokenSource in _dispatchCancellationTokenSources.Values)
            {
                cancellationTokenSource.Cancel();
            }
        }

        /// <inheritdoc/>
        public async Task PingAsync(CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();

            await SendControlFrameAsync(Ice1Definitions.ValidateConnectionFrame, cancel).ConfigureAwait(false);

            _logger.LogSentIce1ValidateConnectionFrame();
        }

        /// <inheritdoc/>
        public async Task<IncomingRequest?> ReceiveRequestAsync(CancellationToken cancel)
        {
            (int requestId, ReadOnlyMemory<byte> buffer) = await ReceiveFrameAsync(cancel).ConfigureAwait(false);

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
                IsOneway = requestId != 0,
                PayloadEncoding =
                    Encoding.FromMajorMinor(requestHeader.PayloadEncodingMajor, requestHeader.PayloadEncodingMinor),
                Deadline = DateTime.MaxValue,
                Payload = payload,
                CancelDispatchSource = new()
            };
            if (requestHeader.Context.Count > 0)
            {
                request.Features = request.Features.WithContext(requestHeader.Context);
            }
            _dispatchCancellationTokenSources[requestId] = request.CancelDispatchSource;
            return request;
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
            if (!_pendingIncomingResponses.TryGetValue(requestId, out TaskCompletionSource<ReadOnlyMemory<byte>>? source))
            {
                throw new InvalidOperationException("unknown request with requestId {requestId}");
            }

            // Wait for the response.
            ReadOnlyMemory<byte> buffer = await source.Task.WaitAsync(cancel).ConfigureAwait(false);

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
            if (request.Fields.Count > 0 || request.FieldsDefaults.Count > 0)
            {
                throw new NotSupportedException($"{nameof(Protocol.Ice1)} doesn't support fields");
            }

            // Wait for sending of other frames to complete. The semaphore is used as an asynchronous queue to
            // serialize the sending of frames.
            await _sendSemaphore.EnterAsync(cancel).ConfigureAwait(false);

            try
            {
                var bufferWriter = new BufferWriter();
                var encoder = new Ice11Encoder(bufferWriter);

                // Write the Ice1 request header.
                bufferWriter.WriteByteSpan(Ice1Definitions.FramePrologue);
                encoder.EncodeIce1FrameType(Ice1FrameType.Request);
                encoder.EncodeByte(0); // compression status
                BufferWriter.Position frameSizeStart = encoder.StartFixedLengthSize();

                int requestId = request.IsOneway ? 0 : ++_nextRequestId;
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

                // Perform the sending. When an an Ice1 frame is sent over a connection (such as a TCP
                // connection), we need to send the entire frame even when cancel gets canceled since the
                // recipient cannot read a partial frame and then keep going.
                ReadOnlyMemory<ReadOnlyMemory<byte>> buffers = bufferWriter.Finish();
                await _socket.SendAsync(buffers, CancellationToken.None).ConfigureAwait(false);

                // Mark the request as sent and keep track of it if it's a two-way request to receive the response.
                request.IsSent = true;
                if (!request.IsOneway)
                {
                    _pendingIncomingResponses[requestId] = new();
                }
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
            if (request.IsOneway)
            {
                throw new InvalidOperationException("can't send a response for a one-way request");
            }
            if (request.Features.GetRequestId() is not int requestId)
            {
                throw new InvalidOperationException("request ID feature is not set");
            }

            if (_dispatchCancellationTokenSources.TryRemove(requestId, out CancellationTokenSource? source))
            {
                source.Dispose();
            }

            if (_shutdown && _dispatchCancellationTokenSources.IsEmpty)
            {
                // All the dispatch completed, notify the shutdown that it can send the CloseConnection frame.
                _dispatchEmptyTaskCompletionSource!.SetResult();
            }

            // Wait for sending of other frames to complete. The semaphore is used as an asynchronous queue to
            // serialize the sending of frames.
            await _sendSemaphore.EnterAsync(cancel).ConfigureAwait(false);

            try
            {
                var bufferWriter = new BufferWriter();
                bufferWriter.WriteByteSpan(request.Stream.TransportHeader.Span);
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
                await _socket.SendAsync(buffers, CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                _sendSemaphore.Release();
            }
        }

        /// <inheritdoc/>
        public async Task ShutdownAsync(bool shutdownByPeer, string message, CancellationToken cancel)
        {
            // Prevent new incoming requests from being accepted.
            _shutdown = true;

            var exception = new ConnectionClosedException(message);
            foreach (TaskCompletionSource<ReadOnlyMemory<byte>> source in _pendingIncomingResponses.Values)
            {
                source.SetException(exception);
            }

            if (shutdownByPeer)
            {
                // Peer sent CloseConnection frame, it's not longer interested in the dispatch so we can
                // cancel them.
                CancelDispatch();
            }
            if (!shutdownByPeer)
            {
                // Wait for incoming streams to complete before sending the CloseConnection frame.
                _dispatchEmptyTaskCompletionSource ??= new TaskCompletionSource();
                await _dispatchEmptyTaskCompletionSource.Task.WaitAsync(cancel).ConfigureAwait(false);

                // Write the CloseConnectionFrame frame.
                await SendControlFrameAsync(Ice1Definitions.CloseConnectionFrame, cancel).ConfigureAwait(false);
            }
        }

        public async Task<string> WaitForShutdownAsync(CancellationToken cancel)
        {
            await _pendingCloseConnection.Task.WaitAsync(cancel).ConfigureAwait(false);
            return "connection graceful shutdown";
        }

        private void CancelDispatch()
        {
            foreach (CancellationTokenSource cancellationTokenSource in _dispatchCancellationTokenSources.Values)
            {
                cancellationTokenSource.Cancel();
            }
        }

        private async ValueTask SendControlFrameAsync(
            ReadOnlyMemory<ReadOnlyMemory<byte>> buffers,
            CancellationToken cancel)
        {
            // Wait for sending of other frames to complete. The semaphore is used as an asynchronous queue to
            // serialize the sending of frames.
            await _sendSemaphore.EnterAsync(cancel).ConfigureAwait(false);

            try
            {
                // Perform the sending.
                await _socket.SendAsync(buffers, CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                _sendSemaphore.Release();
            }
        }

        private async ValueTask<(int, ReadOnlyMemory<byte>)> ReceiveFrameAsync(CancellationToken cancel)
        {
            while (true)
            {
                // Receive the Ice1 frame header.
                Memory<byte> buffer;
                if (_networkConnection.IsDatagram)
                {
                    buffer = new byte[_socket.DatagramMaxReceiveSize];
                    int received = await _socket.ReceiveAsync(buffer, cancel).ConfigureAwait(false);
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
                    if (_networkConnection.IsDatagram)
                    {
                        _logger.LogReceivedInvalidDatagram(frameSize);
                    }
                    else
                    {
                        throw new InvalidDataException($"received ice1 frame with only {frameSize} bytes");
                    }
                    continue; // while
                }
                if (frameSize > _incomingFrameMaxSize)
                {
                    if (_networkConnection.IsDatagram)
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

                // Read the remainder of the frame if needed.
                if (frameSize > buffer.Length)
                {
                    if (_networkConnection.IsDatagram)
                    {
                        _logger.LogDatagramMaximumSizeExceeded(frameSize);
                        continue;
                    }

                    Memory<byte> newBuffer = new byte[frameSize];
                    buffer[0..Ice1Definitions.HeaderSize].CopyTo(newBuffer[0..Ice1Definitions.HeaderSize]);
                    buffer = newBuffer;
                }
                else if (!_networkConnection.IsDatagram)
                {
                    buffer = buffer[0..frameSize];
                }

                if (!_networkConnection.IsDatagram && frameSize > Ice1Definitions.HeaderSize)
                {
                    await ReceiveUntilFullAsync(buffer[Ice1Definitions.HeaderSize..], cancel).ConfigureAwait(false);
                }

                // The magic and version fields have already been checked.
                var frameType = (Ice1FrameType)buffer.Span[8];
                byte compressionStatus = buffer.Span[9];
                if (compressionStatus == 2)
                {
                    throw new NotSupportedException("cannot decompress ice1 frame");
                }

                switch (frameType)
                {
                    case Ice1FrameType.CloseConnection:
                    {
                        if (buffer.Length > 0)
                        {
                            throw new InvalidDataException("");
                        }
                        _pendingCloseConnection.SetResult();
                        break;
                    }

                    case Ice1FrameType.Request:
                    {
                        if (!_shutdown)
                        {
                            int requestId = IceDecoder.DecodeInt(buffer.Span.Slice(Ice1Definitions.HeaderSize, 4));
                            return (requestId, buffer[(Ice1Definitions.HeaderSize + 4)..]);
                        }
                        else
                        {
                            // The connection is shutting down, ignore incoming requests.
                        }
                        break;
                    }

                    case Ice1FrameType.RequestBatch:
                    {
                        int invokeNum = IceDecoder.DecodeInt(buffer.Span.Slice(Ice1Definitions.HeaderSize, 4));
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
                        int requestId = IceDecoder.DecodeInt(buffer.Span.Slice(Ice1Definitions.HeaderSize, 4));
                        if (_pendingIncomingResponses.TryGetValue(
                            requestId,
                            out TaskCompletionSource<ReadOnlyMemory<byte>>? source))
                        {
                            source.SetResult(buffer[(Ice1Definitions.HeaderSize + 4)..]);
                        }
                        break;
                    }

                    case Ice1FrameType.ValidateConnection:
                    {
                        // Notify the control stream of the reception of a Ping frame.
                        _logger.LogReceivedIce1ValidateConnectionFrame();
                        _pingReceived?.Invoke();
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
                offset += await _socket.ReceiveAsync(buffer[offset..], cancel).ConfigureAwait(false);
            }
        }
    }
}
