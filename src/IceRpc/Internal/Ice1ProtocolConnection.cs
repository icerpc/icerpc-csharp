// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features.Internal;
using IceRpc.Slice;
using IceRpc.Slice.Internal;
using IceRpc.Transports;
using IceRpc.Transports.Internal;
using System.Buffers;
using System.Collections.Immutable;
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
        public event Action<string>? PeerShutdownInitiated;

        private readonly TaskCompletionSource _dispatchesAndInvocationsCompleted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly HashSet<IncomingRequest> _dispatches = new();
        private readonly int _incomingFrameMaxSize;
        private readonly Dictionary<int, OutgoingRequest> _invocations = new();
        private readonly bool _isUdp;

        private readonly MemoryPool<byte> _memoryPool;

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
                IDisposable disposable;

                try
                {
                    (requestId, buffer, disposable) = await ReceiveFrameAsync().ConfigureAwait(false);
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

                try
                {
                    Ice1RequestHeader requestHeader = DecodeHeader(ref buffer);

                    if (requestHeader.Identity.Name.Length == 0)
                    {
                        throw new InvalidDataException("received ice1 request with empty identity name");
                    }
                    if (requestHeader.Operation.Length == 0)
                    {
                        throw new InvalidDataException("received request with empty operation name");
                    }

                    // The payload size is the encapsulation size less the 6 bytes of the encapsulation header.
                    int payloadSize = requestHeader.EncapsulationSize - 6;
                    if (payloadSize != buffer.Length - 4)
                    {
                        throw new InvalidDataException(@$"request payload size mismatch: expected {payloadSize
                            } bytes, read {buffer.Length - 4} bytes");
                    }

                    var payloadEncoding = Encoding.FromMajorMinor(
                        requestHeader.PayloadEncodingMajor,
                        requestHeader.PayloadEncodingMinor);

                    EncodePayloadSize(payloadSize, payloadEncoding, buffer.Span[0..4]);

                    var request = new IncomingRequest(
                        Protocol.Ice1,
                        path: requestHeader.Identity.ToPath(),
                        fragment: new Facet(requestHeader.OptionalFacet).ToString(),
                        operation: requestHeader.Operation,
                        payload: new DisposableSequencePipeReader(new ReadOnlySequence<byte>(buffer), disposable),
                        payloadEncoding,
                        responseWriter: requestId == 0 ?
                            InvalidPipeWriter.Instance : new SimpleNetworkConnectionPipeWriter(_networkConnection))
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
                        if (_shutdown)
                        {
                            disposable.Dispose();
                            // and loop back
                        }
                        else
                        {
                            _dispatches.Add(request);
                            request.CancelDispatchSource = new();
                            return request;
                        }
                    }
                }
                catch
                {
                    disposable.Dispose();
                    throw;
                }
            }

            static Ice1RequestHeader DecodeHeader(ref Memory<byte> buffer)
            {
                var decoder = new IceDecoder(buffer, Encoding.Ice11);
                var requestHeader = new Ice1RequestHeader(ref decoder);

                // The payload plus 4 bytes from the encapsulation header used to store the payload size encoded
                // with payload encoding.
                buffer = buffer[((int)decoder.Consumed - 4)..];

                return requestHeader;
            }
        }

        /// <inheritdoc/>
        public async Task<IncomingResponse> ReceiveResponseAsync(OutgoingRequest request, CancellationToken cancel)
        {
            // This class sent this request and didn't set a ResponseReader on it.
            Debug.Assert(request.ResponseReader == null);
            Debug.Assert(!request.IsOneway);

            Ice1Request? requestFeature = request.Features.Get<Ice1Request>();
            if (requestFeature == null || requestFeature.ResponseCompletionSource == null)
            {
                throw new InvalidOperationException("unknown request");
            }

            // Wait for the response.

            Memory<byte> buffer;
            IDisposable disposable;

            try
            {
                (buffer, disposable) = await requestFeature.ResponseCompletionSource.Task.WaitAsync(
                    cancel).ConfigureAwait(false);
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

            try
            {
                (ReplyStatus replyStatus, int payloadSize, Encoding payloadEncoding) = DecodeHeader(ref buffer);

                ResultType resultType = replyStatus == ReplyStatus.OK ? ResultType.Success : ResultType.Failure;

                // We write the payload size in the first 4 bytes of the buffer.
                EncodePayloadSize(payloadSize, payloadEncoding, buffer.Span[0..4]);

                FeatureCollection features = FeatureCollection.Empty;

                // For compatibility with ZeroC Ice
                if (replyStatus == ReplyStatus.ObjectNotExistException &&
                    (request.Proxy.Endpoint == null || request.Proxy.Endpoint.Transport == TransportNames.Loc)) // "indirect" proxy
                {
                    features = features.With(RetryPolicy.OtherReplica);
                }

                return new IncomingResponse(
                    Protocol.Ice1,
                    resultType,
                    new DisposableSequencePipeReader(new ReadOnlySequence<byte>(buffer), disposable),
                    payloadEncoding)
                {
                    Features = features,
                };
            }
            catch
            {
                disposable.Dispose();
                throw;
            }

            static (ReplyStatus ReplyStatus, int PayloadSize, Encoding PayloadEncoding) DecodeHeader(
                ref Memory<byte> buffer)
            {
                // Decode the response.
                var decoder = new IceDecoder(buffer, Encoding.Ice11);

                // we keep 4 extra bytes in the response buffer to be able to write the payload size before an ice1
                // system exception
                decoder.Skip(4);

                ReplyStatus replyStatus = decoder.DecodeReplyStatus();

                int payloadSize;
                Encoding payloadEncoding;

                if (replyStatus <= ReplyStatus.UserException)
                {
                    var responseHeader = new Ice1ResponseHeader(ref decoder);
                    payloadSize = responseHeader.EncapsulationSize - 6;
                    payloadEncoding = Encoding.FromMajorMinor(
                        responseHeader.PayloadEncodingMajor,
                        responseHeader.PayloadEncodingMinor);

                    if (payloadEncoding == Encoding.Ice11 && replyStatus == ReplyStatus.UserException)
                    {
                        buffer = buffer[((int)decoder.Consumed - 5)..];

                        // We encode the reply status (UserException) right after the payload size
                        buffer.Span[4] = (byte)ReplyStatus.UserException;
                        payloadSize += 1; // for the additional reply status
                    }
                    else
                    {
                        buffer = buffer[((int)decoder.Consumed - 4)..]; // no reply status
                    }

                    if (payloadSize != buffer.Length - 4)
                    {
                        throw new InvalidDataException(@$"response payload size mismatch: expected {payloadSize
                            } bytes, read {buffer.Length - 4} bytes");
                    }
                }
                else
                {
                    // Ice1 system exception
                    payloadSize = buffer.Length - 4; // includes reply status, excludes the payload size
                    payloadEncoding = Encoding.Ice11;
                    // buffer stays the same
                }

                return (replyStatus, payloadSize, payloadEncoding);
            }
        }

        /// <inheritdoc/>
        public async Task SendRequestAsync(OutgoingRequest request, CancellationToken cancel)
        {
            if (request.PayloadEncoding is not IceEncoding payloadEncoding)
            {
                throw new NotSupportedException(
                    "the payload of an ice1 request must be encoded with a supported Slice encoding");
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
                (int payloadSize, bool isCanceled, bool isCompleted) = await payloadEncoding.DecodeSegmentSizeAsync(
                    request.PayloadSource,
                    cancel).ConfigureAwait(false);

                if (isCanceled)
                {
                    throw new OperationCanceledException();
                }

                if (payloadSize > 0 && isCompleted)
                {
                    throw new ArgumentException(
                        $"expected {payloadSize} bytes in ice1 request payload source, but it's empty");
                }

                AsyncCompletePipeWriter output = _isUdp ? new UdpPipeWriter(_networkConnection) :
                    new SimpleNetworkConnectionPipeWriter(_networkConnection);

                EncodeHeader(output, payloadSize);
                request.InitialPayloadSink.SetDecoratee(output);

                // TODO: it would make sense to pass the known payloadSize to SendPayloadAsync
                await SendPayloadAsync(request, output, cancel).ConfigureAwait(false);
                request.IsSent = true;
            }
            catch (ObjectDisposedException exception)
            {
                // If the network connection has been disposed, we raise ConnectionLostException to ensure the
                // request is retried by the retry interceptor.
                // TODO: this is clunky but required for retries to work because the retry interceptor only retries
                // a request if the exception is a transport exception.
                var ex = new ConnectionLostException(exception);
                await request.PayloadSource.CompleteAsync(ex).ConfigureAwait(false);
                await request.PayloadSink.CompleteAsync(ex).ConfigureAwait(false);
                throw ex;
            }
            catch (Exception ex)
            {
                await request.PayloadSource.CompleteAsync(ex).ConfigureAwait(false);
                await request.PayloadSink.CompleteAsync(ex).ConfigureAwait(false);
                throw;
            }
            finally
            {
                _sendSemaphore.Release();
            }

            void EncodeHeader(AsyncCompletePipeWriter output, int payloadSize)
            {
                var encoder = new IceEncoder(output, Encoding.Ice11);

                // Write the Ice1 request header.
                encoder.WriteByteSpan(Ice1Definitions.FramePrologue);
                encoder.EncodeIce1FrameType(Ice1FrameType.Request);
                encoder.EncodeByte(0); // compression status

                Memory<byte> sizePlaceholder = encoder.GetPlaceholderMemory(4);

                encoder.EncodeInt(requestId);
                (byte encodingMajor, byte encodingMinor) = payloadEncoding.ToMajorMinor();

                var requestHeader = new Ice1RequestHeader(
                    IceIdentity.FromPath(request.Path),
                    Facet.FromString(request.Fragment).Value,
                    request.Operation,
                    request.IsIdempotent ? OperationMode.Idempotent : OperationMode.Normal,
                    request.Features.GetContext(),
                    encapsulationSize: payloadSize + 6,
                    encodingMajor,
                    encodingMinor);
                requestHeader.Encode(ref encoder);

                IceEncoder.EncodeInt(encoder.EncodedByteCount + payloadSize, sizePlaceholder.Span);
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
                if (request.IsOneway)
                {
                    await response.PayloadSource.CompleteAsync().ConfigureAwait(false);
                    await response.PayloadSink.CompleteAsync().ConfigureAwait(false);
                }
                else
                {
                    Debug.Assert(!_isUdp); // udp is oneway-only so no response

                    // Wait for sending of other frames to complete. The semaphore is used as an asynchronous
                    // queue to serialize the sending of frames.
                    await _sendSemaphore.EnterAsync(cancel).ConfigureAwait(false);
                    try
                    {
                        if (request.PayloadEncoding is not IceEncoding payloadEncoding)
                        {
                            throw new NotSupportedException(
                                "the payload of an ice1 request must be encoded with a supported Slice encoding");
                        }

                        (int payloadSize, bool isCanceled, bool isCompleted) =
                            await payloadEncoding.DecodeSegmentSizeAsync(
                                response.PayloadSource,
                                cancel).ConfigureAwait(false);

                        if (isCanceled)
                        {
                            throw new OperationCanceledException();
                        }

                        if (payloadSize > 0 && isCompleted)
                        {
                            throw new ArgumentException(
                                $"expected {payloadSize} bytes in response payload source, but it's empty");
                        }

                        ReplyStatus replyStatus = ReplyStatus.OK;

                        if (response.ResultType == ResultType.Failure)
                        {
                            if (payloadEncoding == Encoding.Ice11)
                            {
                                // extract reply status from 1.1-encoded payload
                                ReadResult readResult = await response.PayloadSource.ReadAsync(
                                    cancel).ConfigureAwait(false);

                                if (readResult.IsCanceled)
                                {
                                    throw new OperationCanceledException();
                                }
                                if (readResult.Buffer.IsEmpty)
                                {
                                    throw new ArgumentException("empty exception payload");
                                }

                                replyStatus = (ReplyStatus)readResult.Buffer.FirstSpan[0];
                                response.PayloadSource.AdvanceTo(readResult.Buffer.GetPosition(1));
                                payloadSize -= 1;
                            }
                            else
                            {
                                replyStatus = ReplyStatus.UserException;
                            }
                        }

                        EncodeHeader(payloadEncoding, payloadSize, replyStatus);

                        // TODO: it would make sense to pass the known payloadSize to SendPayloadAsync
                        await SendPayloadAsync(
                            response,
                            (AsyncCompletePipeWriter)request.ResponseWriter,
                            cancel).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        await response.PayloadSource.CompleteAsync(ex).ConfigureAwait(false);
                        await response.PayloadSink.CompleteAsync(ex).ConfigureAwait(false);
                        throw;
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

            void EncodeHeader(IceEncoding payloadEncoding, int payloadSize, ReplyStatus replyStatus)
            {
                var encoder = new IceEncoder(request.ResponseWriter, Encoding.Ice11);

                // Write the response header.

                encoder.WriteByteSpan(Ice1Definitions.FramePrologue);
                encoder.EncodeIce1FrameType(Ice1FrameType.Reply);
                encoder.EncodeByte(0); // compression status
                Memory<byte> sizePlaceholder = encoder.GetPlaceholderMemory(4);

                encoder.EncodeInt(requestId);
                (byte encodingMajor, byte encodingMinor) = payloadEncoding.ToMajorMinor();

                encoder.EncodeReplyStatus(replyStatus);
                if (replyStatus <= ReplyStatus.UserException)
                {
                    var responseHeader = new Ice1ResponseHeader(
                        encapsulationSize: payloadSize + 6,
                        encodingMajor,
                        encodingMinor);
                    responseHeader.Encode(ref encoder);
                }

                IceEncoder.EncodeInt(encoder.EncodedByteCount + payloadSize, sizePlaceholder.Span);
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
                }

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
            _memoryPool = MemoryPool<byte>.Shared; // TODO: should be configurable by caller
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
                    int frameSize = Ice11Encoding.DecodeFixedLengthSize(buffer.AsReadOnlySpan().Slice(10, 4));
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

        /// <summary>Sends the payload source of an outgoing frame.</summary>
        private async ValueTask SendPayloadAsync(
            OutgoingFrame outgoingFrame,
            AsyncCompletePipeWriter frameWriter,
            CancellationToken cancel)
        {
            if (outgoingFrame.PayloadSourceStream != null)
            {
                // Since the payload is encoded with a Slice encoding, PayloadSourceStream can only come from
                // a Slice stream parameter/return.
                throw new NotSupportedException("stream parameters and return values are not supported with ice1");
            }

            // We first fetch the full payload with the cancellation token.
            while (true)
            {
                ReadResult readResult = await outgoingFrame.PayloadSource.ReadAsync(cancel).ConfigureAwait(false);

                // Don't consume anything
                outgoingFrame.PayloadSource.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);

                // Keep going until we've read the full payload.
                if (readResult.IsCompleted)
                {
                    break;
                }
            }

            // Next we switch to CancellationToken.None unless we use UDP
            if (!_isUdp)
            {
                cancel = CancellationToken.None;
            }
            frameWriter.CompleteCancellationToken = cancel;

            FlushResult flushResult = await outgoingFrame.PayloadSink.CopyFromAsync(
                outgoingFrame.PayloadSource,
                completeWhenDone: true,
                cancel).ConfigureAwait(false);

            Debug.Assert(!flushResult.IsCanceled); // not implemented
            Debug.Assert(!flushResult.IsCompleted); // the reader can't reject the frame without triggering an exception

            await outgoingFrame.PayloadSource.CompleteAsync().ConfigureAwait(false);
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
                Ice20Encoding.EncodeSize(payloadSize, buffer);
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

        private async ValueTask<(int RequestId, Memory<byte> Buffer, IDisposable Disposable)> ReceiveFrameAsync()
        {
            // Reads are not cancellable. This method returns once a frame is read or when the connection is disposed.
            CancellationToken cancel = CancellationToken.None;

            IMemoryOwner<byte>? memoryOwner = null;

            try
            {
                while (true)
                {
                    // Recycle
                    memoryOwner?.Dispose();
                    memoryOwner = null;

                    Memory<byte> buffer;

                    // Receive the Ice1 frame header.
                    if (_isUdp)
                    {
                        memoryOwner = _memoryPool.Rent(_incomingFrameMaxSize);

                        int received = await _networkConnection.ReadAsync(
                            memoryOwner.Memory, cancel).ConfigureAwait(false);
                        if (received < Ice1Definitions.HeaderSize)
                        {
                            // TODO: implement protocol logging with decorators
                            //_logger.LogReceivedInvalidDatagram(received);
                            continue; // while
                        }
                        buffer = memoryOwner.Memory[0..received];
                    }
                    else
                    {
                        memoryOwner = _memoryPool.Rent(256);
                        await ReceiveUntilFullAsync(
                            memoryOwner.Memory[0..Ice1Definitions.HeaderSize], cancel).ConfigureAwait(false);

                        buffer = memoryOwner.Memory[0..Ice1Definitions.HeaderSize];
                    }

                    // Check the header
                    Ice1Definitions.CheckHeader(buffer.Span[0..Ice1Definitions.HeaderSize]);
                    int frameSize = Ice11Encoding.DecodeFixedLengthSize(buffer[10..14].Span);
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

                    // The magic and version fields have already been checked by CheckHeader above
                    var frameType = (Ice1FrameType)buffer.Span[8];
                    byte compressionStatus = buffer.Span[9];
                    if (compressionStatus == 2)
                    {
                        throw new NotSupportedException("cannot decompress ice1 frame");
                    }

                    // Read the remainder of the frame if needed.
                    if (_isUdp)
                    {
                        // Remove header
                        buffer = buffer[Ice1Definitions.HeaderSize..];
                    }
                    else if (frameSize == Ice1Definitions.HeaderSize)
                    {
                        buffer = Memory<byte>.Empty;
                    }
                    else
                    {
                        int remainingSize = frameSize - Ice1Definitions.HeaderSize;

                        if (memoryOwner.Memory.Length < remainingSize)
                        {
                            memoryOwner.Dispose();
                            memoryOwner = _memoryPool.Rent(remainingSize);
                        }

                        await ReceiveUntilFullAsync(memoryOwner.Memory[0..remainingSize], cancel).ConfigureAwait(false);
                        buffer = memoryOwner.Memory[0..remainingSize];
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
                                PeerShutdownInitiated?.Invoke("connection shutdown by peer");
                            }
                            catch (Exception ex)
                            {
                                Debug.Assert(
                                    false,
                                    $"{nameof(PeerShutdownInitiated)} raised unexpected exception\n{ex}");
                            }

                            var exception = new ConnectionClosedException("connection shutdown by peer");

                            // The peer cancels its invocations on shutdown so we can cancel the dispatches.
                            CancelDispatches();

                            // The peer didn't dispatch invocations which are still in progress, these invocations can
                            // therefore be retried (completing the invocation here ensures that the invocations won't
                            // get ConnectionLostException from Dispose).
                            CancelInvocations(exception);

                            // New requests will complete with ConnectionClosedException.
                            _sendSemaphore.Complete(exception);

                            throw exception;
                        }

                        case Ice1FrameType.Request:
                        {
                            int requestId = Ice11Encoding.DecodeFixedLengthSize(buffer.Span[0..4]);
                            buffer = buffer[4..]; // consume these 4 bytes
                            return (requestId, buffer, memoryOwner);
                        }

                        case Ice1FrameType.RequestBatch:
                        {
                            int invokeNum = Ice11Encoding.DecodeFixedLengthSize(buffer.Span[0..4]);

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
                            int requestId = Ice11Encoding.DecodeFixedLengthSize(buffer.Span[0..4]);
                            // we keep these 4 bytes in buffer

                            lock (_mutex)
                            {
                                if (_invocations.TryGetValue(requestId, out OutgoingRequest? request))
                                {
                                    request.Features.Get<Ice1Request>()!.ResponseCompletionSource!.SetResult(
                                        (buffer, memoryOwner));
                                    memoryOwner = null; // otherwise memoryOwner is disposed immediately
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
                } // while
            }
            catch
            {
                memoryOwner?.Dispose();
                throw;
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
