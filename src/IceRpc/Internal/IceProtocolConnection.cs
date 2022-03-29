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
    internal sealed class IceProtocolConnection : IProtocolConnection
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
        public ImmutableDictionary<ConnectionFieldKey, ReadOnlySequence<byte>> PeerFields =>
            ImmutableDictionary<ConnectionFieldKey, ReadOnlySequence<byte>>.Empty;

        /// <inheritdoc/>
        public event Action<string>? PeerShutdownInitiated;

        private static readonly IDictionary<RequestFieldKey, ReadOnlySequence<byte>> _idempotentFields =
            new Dictionary<RequestFieldKey, ReadOnlySequence<byte>>
            {
                [RequestFieldKey.Idempotent] = default
            }.ToImmutableDictionary();

        private readonly TaskCompletionSource _dispatchesAndInvocationsCompleted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly HashSet<CancellationTokenSource> _dispatches = new();
        private readonly Dictionary<int, TaskCompletionSource<PipeReader>> _invocations = new();
        private readonly bool _isUdp;

        private readonly MemoryPool<byte> _memoryPool;
        private readonly int _minimumSegmentSize;

        private readonly object _mutex = new();
        private readonly SimpleNetworkConnectionReader _networkConnectionReader;
        private readonly SimpleNetworkConnectionWriter _networkConnectionWriter;

        private int _nextRequestId;
        private readonly Configure.IceProtocolOptions _options;
        private readonly IcePayloadPipeWriter _payloadWriter;
        private readonly TaskCompletionSource _pendingClose = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly AsyncSemaphore _sendSemaphore = new(1, 1);
        private bool _shuttingDown;

        public async Task AcceptRequestsAsync(Connection connection, IDispatcher dispatcher)
        {
            while (true)
            {
                // Wait for a request frame to be received.
                int requestFrameSize; // the total size of the request frame
                try
                {
                    requestFrameSize = await ReceiveFrameAsync().ConfigureAwait(false);
                }
                catch
                {
                    lock (_mutex)
                    {
                        // The connection was gracefully shut down, raise ConnectionClosedException here to ensure
                        // that the ClosedEvent will report this exception instead of the transport failure.
                        if (_shuttingDown && _invocations.Count == 0 && _dispatches.Count == 0)
                        {
                            throw new ConnectionClosedException("connection gracefully shut down");
                        }
                        else
                        {
                            throw;
                        }
                    }
                }

                // Read the request frame.
                PipeReader requestFrameReader = await CreateFrameReaderAsync(
                    requestFrameSize - IceDefinitions.PrologueSize,
                    _networkConnectionReader,
                    _memoryPool,
                    _minimumSegmentSize,
                    CancellationToken.None).ConfigureAwait(false);

                // Decode its header.
                int requestId;
                IceRequestHeader requestHeader;
                try
                {
                    if (!requestFrameReader.TryRead(out ReadResult readResult))
                    {
                        throw new InvalidDataException("received invalid request frame");
                    }

                    Debug.Assert(readResult.IsCompleted);

                    (requestId, requestHeader, int consumed) = DecodeRequestIdAndHeader(readResult.Buffer);
                    requestFrameReader.AdvanceTo(readResult.Buffer.GetPosition(consumed));
                }
                catch
                {
                    await requestFrameReader.CompleteAsync().ConfigureAwait(false);
                    throw;
                }

                var request = new IncomingRequest(Protocol.Ice)
                {
                    Connection = connection,
                    Fields = requestHeader.OperationMode == OperationMode.Normal ?
                        ImmutableDictionary<RequestFieldKey, ReadOnlySequence<byte>>.Empty : _idempotentFields,
                    Fragment = requestHeader.Fragment,
                    IsOneway = requestId == 0,
                    Operation = requestHeader.Operation,
                    Path = requestHeader.Path,
                    Payload = requestFrameReader,
                    PayloadEncoding = Encoding.FromMajorMinor(
                        requestHeader.EncapsulationHeader.PayloadEncodingMajor,
                        requestHeader.EncapsulationHeader.PayloadEncodingMinor),
                    ResponseWriter = _payloadWriter,
                };

                if (requestHeader.Context.Count > 0)
                {
                    request.Features = request.Features.WithContext(requestHeader.Context);
                }

                // TODO: at this point - before reading the actual request frame - we could easily wait until
                // _dispatches.Count < MaxConcurrentDispatches.

                _ = Task.Run(() => DispatchRequestAsync(requestId, request));
            }

            async Task DispatchRequestAsync(int requestId, IncomingRequest request)
            {
                using var cancelDispatchSource = new CancellationTokenSource();

                bool shuttingDown = false;
                lock (_mutex)
                {
                    if (_shuttingDown)
                    {
                        shuttingDown = true;
                    }
                    else
                    {
                        _dispatches.Add(cancelDispatchSource);
                    }
                }

                if (shuttingDown)
                {
                    // If shutting down, ignore the incoming request.
                    // TODO: replace with payload exception and error code
                    var exception = new ConnectionClosedException();
                    await request.Payload.CompleteAsync(exception).ConfigureAwait(false);
                    return;
                }

                OutgoingResponse response;
                try
                {
                    // The dispatcher is responsible for completing the incoming request payload source.
                    response = await dispatcher.DispatchAsync(
                        request,
                        cancelDispatchSource.Token).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    // If we catch an exception, we return a failure response with a Slice-encoded payload.

                    if (exception is OperationCanceledException)
                    {
                        exception = new DispatchException("dispatch canceled by peer", DispatchErrorCode.Canceled);
                    }

                    // With the ice protocol, a ResultType = Failure exception must be an ice system exception.
                    if (exception is not RemoteException remoteException ||
                        remoteException.ConvertToUnhandled ||
                        remoteException is not DispatchException)
                    {
                        remoteException = new DispatchException(
                            message: null,
                            exception is InvalidDataException ?
                                DispatchErrorCode.InvalidData : DispatchErrorCode.UnhandledException,
                            exception);
                    }

                    response = new OutgoingResponse(request)
                    {
                        PayloadSource = Encoding.Slice11.CreatePayloadFromRemoteException(remoteException),
                        ResultType = ResultType.Failure
                    };
                }

                // The sending of the response can't be canceled. This would lead to invalid protocol behavior.
                CancellationToken cancel = CancellationToken.None;

                bool acquiredSemaphore = false;
                try
                {
                    if (response.PayloadSourceStream != null)
                    {
                        throw new NotSupportedException("PayloadSourceStream must be null with the ice protocol");
                    }

                    if (request.IsOneway)
                    {
                        await response.CompleteAsync().ConfigureAwait(false);
                        return;
                    }

                    Debug.Assert(!_isUdp); // udp is oneway-only so no response

                    // Read the full payload source. This can take some time so this needs to be done before acquiring
                    // the send semaphore.
                    ReadOnlySequence<byte> payload = await ReadFullPayloadAsync(
                        response.PayloadSource,
                        cancel).ConfigureAwait(false);
                    int payloadSize = checked((int)payload.Length);

                    // Wait for sending of other frames to complete. The semaphore is used as an asynchronous queue to
                    // serialize the sending of frames.
                    await _sendSemaphore.EnterAsync(cancel).ConfigureAwait(false);
                    acquiredSemaphore = true;

                    ReplyStatus replyStatus = ReplyStatus.OK;

                    if (response.ResultType != ResultType.Success)
                    {
                        if (response.ResultType == ResultType.Failure)
                        {
                            replyStatus = payload.FirstSpan[0].AsReplyStatus();

                            if (replyStatus <= ReplyStatus.UserException)
                            {
                                throw new InvalidDataException(
                                    $"unexpected reply status value '{replyStatus}' in payload");
                            }
                        }
                        else
                        {
                            replyStatus = ReplyStatus.UserException;
                        }
                    }

                    EncodeResponseHeader(_networkConnectionWriter, requestId, payloadSize, replyStatus);

                    // Write the payload and complete the source.
                    FlushResult flushResult = await response.PayloadSink.WriteAsync(
                        payload,
                        endStream: false,
                        cancel).ConfigureAwait(false);

                    // If a payload sink decorator returns a canceled or completed flush result, we have to raise
                    // NotSupportedException. We can't interrupt the sending of a payload since it would lead to a bogus
                    // payload to be sent over the connection.
                    if (flushResult.IsCanceled || flushResult.IsCompleted)
                    {
                        throw new NotSupportedException(
                            "payload sink cancellation or completion is not supported with the ice protocol");
                    }

                    await response.PayloadSource.CompleteAsync().ConfigureAwait(false);
                    await response.PayloadSink.CompleteAsync().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    await response.CompleteAsync(exception).ConfigureAwait(false);
                    throw;
                }
                finally
                {
                    if (acquiredSemaphore)
                    {
                        _sendSemaphore.Release();
                    }

                    lock (_mutex)
                    {
                        // Dispatch is done, remove the cancellation token source for the dispatch.
                        if (_dispatches.Remove(cancelDispatchSource))
                        {
                            // If no more invocations or dispatches and shutting down, shutdown can complete.
                            if (_shuttingDown && _invocations.Count == 0 && _dispatches.Count == 0)
                            {
                                _dispatchesAndInvocationsCompleted.TrySetResult();
                            }
                        }
                    }
                }

                static void EncodeResponseHeader(
                    SimpleNetworkConnectionWriter writer,
                    int requestId,
                    int payloadSize,
                    ReplyStatus replyStatus)
                {
                    var encoder = new SliceEncoder(writer, Encoding.Slice11);

                    // Write the response header.

                    encoder.WriteByteSpan(IceDefinitions.FramePrologue);
                    encoder.EncodeIceFrameType(IceFrameType.Reply);
                    encoder.EncodeByte(0); // compression status
                    Memory<byte> sizePlaceholder = encoder.GetPlaceholderMemory(4);

                    encoder.EncodeInt(requestId);

                    if (replyStatus <= ReplyStatus.UserException)
                    {
                        encoder.EncodeReplyStatus(replyStatus);

                        // When IceRPC receives a response, it ignores the response encoding. So this "1.1" is only relevant
                        // to a ZeroC Ice client that decodes the response. The only Slice encoding such a client can
                        // possibly use to decode the response payload is 1.1 or 1.0, and we don't care about interop with
                        // 1.0.
                        var encapsulationHeader = new EncapsulationHeader(
                            encapsulationSize: payloadSize + 6,
                            payloadEncodingMajor: 1,
                            payloadEncodingMinor: 1);
                        encapsulationHeader.Encode(ref encoder);
                    }
                    // else the reply status (> UserException) is part of the payload

                    int frameSize = encoder.EncodedByteCount + payloadSize;
                    SliceEncoder.EncodeInt(frameSize, sizePlaceholder.Span);
                }
            }

            static (int RequestId, IceRequestHeader Header, int Consumed) DecodeRequestIdAndHeader(
                ReadOnlySequence<byte> buffer)
            {
                var decoder = new SliceDecoder(buffer, Encoding.Slice11);

                int requestId = decoder.DecodeInt();
                var requestHeader = new IceRequestHeader(ref decoder);

                int payloadSize = requestHeader.EncapsulationHeader.EncapsulationSize - 6;
                if (payloadSize != (buffer.Length - decoder.Consumed))
                {
                    throw new InvalidDataException(@$"request payload size mismatch: expected {payloadSize
                        } bytes, read {buffer.Length - decoder.Consumed} bytes");
                }

                return (requestId, requestHeader, (int)decoder.Consumed);
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            _networkConnectionReader.Dispose();
            _networkConnectionWriter.Dispose();

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
                EncodeValidateConnectionFrame(_networkConnectionWriter);
                // The flush can't be canceled because it would lead to the writing of an incomplete frame.
                await _networkConnectionWriter.FlushAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                _sendSemaphore.Release();
            }

            static void EncodeValidateConnectionFrame(SimpleNetworkConnectionWriter writer)
            {
                var encoder = new SliceEncoder(writer, Encoding.Slice11);
                IceDefinitions.ValidateConnectionFrame.Encode(ref encoder);
            }
        }

        public async Task<IncomingResponse> SendRequestAsync(
            Connection connection,
            OutgoingRequest request,
            CancellationToken cancel)
        {
            bool acquiredSemaphore = false;
            int requestId = 0;
            TaskCompletionSource<PipeReader>? responseCompletionSource = null;
            try
            {
                if (request.PayloadSourceStream != null)
                {
                    throw new NotSupportedException("PayloadSourceStream must be null with the ice protocol");
                }

                if (_isUdp && !request.IsOneway)
                {
                    throw new InvalidOperationException("cannot send twoway request over UDP");
                }

                // Set the transport payload sink to the stateless payload writer.
                request.SetTransportPayloadSink(_payloadWriter);

                // Read the full payload source. This can take some time so this needs to be done before acquiring the
                // send semaphore.
                ReadOnlySequence<byte> payload = await ReadFullPayloadAsync(
                    request.PayloadSource,
                    cancel).ConfigureAwait(false);
                int payloadSize = checked((int)payload.Length);

                // Wait for sending of other frames to complete. The semaphore is used as an asynchronous queue to
                // serialize the sending of frames.
                await _sendSemaphore.EnterAsync(cancel).ConfigureAwait(false);
                acquiredSemaphore = true;

                // Assign the request ID for twoway invocations and keep track of the invocation for receiving the
                // response. The request ID is only assigned once the send semaphore is acquired. We don't want a
                // canceled request to allocate a request ID that won't be used.
                if (!request.IsOneway)
                {
                    lock (_mutex)
                    {
                        if (_shuttingDown)
                        {
                            throw new ConnectionClosedException();
                        }
                        requestId = ++_nextRequestId;
                        responseCompletionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
                        _invocations[requestId] = responseCompletionSource;
                    }
                }

                EncodeRequestHeader(_networkConnectionWriter, request, requestId, payloadSize);

                FlushResult flushResult = await request.PayloadSink.WriteAsync(
                    payload,
                    endStream: false,
                    cancel).ConfigureAwait(false);

                // If a payload source sink decorator returns a canceled or completed flush result, we have to raise
                // NotSupportedException. We can't interrupt the sending of a payload since it would lead to a bogus
                // payload to be sent over the connection.
                if (flushResult.IsCanceled || flushResult.IsCompleted)
                {
                    // TODO: throwing here after sending the request is wrong since ReceiveResponse won't be called see
                    // #828 for a solution.
                    throw new NotSupportedException(
                        "payload sink cancellation or completion is not supported with the ice protocol");
                }

                await request.PayloadSink.CompleteAsync().ConfigureAwait(false);
                await request.PayloadSource.CompleteAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                // If the network connection has been disposed, we raise ConnectionLostException to ensure the request
                // is retried by the retry interceptor. The retry interceptor only retries a request if the exception is
                // a transport exception.
                if (exception is ObjectDisposedException)
                {
                    exception = new ConnectionLostException(exception);
                }
                await request.CompleteAsync(exception).ConfigureAwait(false);
                throw;
            }
            finally
            {
                if (acquiredSemaphore)
                {
                    _sendSemaphore.Release();
                }
            }

            // Request is sent at this point.
            request.IsSent = true;

            if (request.IsOneway)
            {
                // We're done, there's no response for oneway requests.
                return new IncomingResponse(request);
            }

            Debug.Assert(responseCompletionSource != null);

            // Wait to receive the response.
            try
            {
                PipeReader frameReader = await responseCompletionSource.Task.WaitAsync(cancel).ConfigureAwait(false);

                try
                {
                    if (!frameReader.TryRead(out ReadResult readResult))
                    {
                        throw new InvalidDataException($"received empty response frame for request #{requestId}");
                    }

                    Debug.Assert(readResult.IsCompleted);

                    ReplyStatus replyStatus = readResult.Buffer.FirstSpan[0].AsReplyStatus();

                    if (replyStatus <= ReplyStatus.UserException)
                    {
                        const int headerSize = 7; // reply status byte + encapsulation header

                        // read and check encapsulation header (6 bytes long)

                        if (readResult.Buffer.Length < headerSize)
                        {
                            throw new ConnectionLostException();
                        }

                        EncapsulationHeader encapsulationHeader = Encoding.Slice11.DecodeBuffer(
                            readResult.Buffer.Slice(1, 6),
                            (ref SliceDecoder decoder) => new EncapsulationHeader(ref decoder));

                        // Sanity check
                        int payloadSize = encapsulationHeader.EncapsulationSize - 6;
                        if (payloadSize != readResult.Buffer.Length - headerSize)
                        {
                            throw new InvalidDataException(
                                @$"response payload size/frame size mismatch: payload size is {payloadSize
                                } bytes but frame has {readResult.Buffer.Length - headerSize} bytes left");
                        }

                        // TODO: check encoding is 1.1. See github proposal.

                        // Consume header.
                        frameReader.AdvanceTo(readResult.Buffer.GetPosition(headerSize));
                    }
                    else
                    {
                        // An ice system exception. The reply status is part of the payload.

                        // Don't consume anything. The examined is irrelevant since readResult.IsCompleted is true.
                        frameReader.AdvanceTo(readResult.Buffer.Start);
                    }

                    // For compatibility with ZeroC Ice "indirect" proxies
                    if (replyStatus == ReplyStatus.ObjectNotExistException && request.Proxy.Endpoint == null)
                    {
                        request.Features = request.Features.With(RetryPolicy.OtherReplica);
                    }

                    return new IncomingResponse(request)
                    {
                        Connection = connection,
                        Payload = frameReader,
                        ResultType = replyStatus switch
                        {
                            ReplyStatus.OK => ResultType.Success,
                            ReplyStatus.UserException => (ResultType)SliceResultType.ServiceFailure,
                            _ => ResultType.Failure
                        }
                    };
                }
                catch (Exception exception)
                {
                    await frameReader.CompleteAsync(exception).ConfigureAwait(false);
                    throw;
                }
            }
            finally
            {
                lock (_mutex)
                {
                    if (_invocations.Remove(requestId))
                    {
                        // If no more invocations or dispatches and shutting down, shutdown can complete.
                        if (_shuttingDown && _invocations.Count == 0 && _dispatches.Count == 0)
                        {
                            _dispatchesAndInvocationsCompleted.TrySetResult();
                        }
                    }
                }
            }

            static void EncodeRequestHeader(
                SimpleNetworkConnectionWriter output,
                OutgoingRequest request,
                int requestId,
                int payloadSize)
            {
                var encoder = new SliceEncoder(output, Encoding.Slice11);

                // Write the request header.
                encoder.WriteByteSpan(IceDefinitions.FramePrologue);
                encoder.EncodeIceFrameType(IceFrameType.Request);
                encoder.EncodeByte(0); // compression status

                Memory<byte> sizePlaceholder = encoder.GetPlaceholderMemory(4);

                encoder.EncodeInt(requestId);

                byte encodingMajor = 1;
                byte encodingMinor = 1;

                // TODO: temporary
                if (request.PayloadEncoding is SliceEncoding payloadEncoding)
                {
                    (encodingMajor, encodingMinor) = payloadEncoding.ToMajorMinor();
                }
                // else remain 1.1

                var requestHeader = new IceRequestHeader(
                    request.Proxy.Path,
                    request.Proxy.Fragment,
                    request.Operation,
                    request.Fields.ContainsKey(RequestFieldKey.Idempotent) ?
                        OperationMode.Idempotent : OperationMode.Normal,
                    request.Features.GetContext(),
                    new EncapsulationHeader(encapsulationSize: payloadSize + 6, encodingMajor, encodingMinor));
                requestHeader.Encode(ref encoder);

                int frameSize = checked(encoder.EncodedByteCount + payloadSize);
                SliceEncoder.EncodeInt(frameSize, sizePlaceholder.Span);
            }
        }

        public async Task ShutdownAsync(string message, CancellationToken cancel)
        {
            var exception = new ConnectionClosedException(message);
            if (_isUdp)
            {
                lock (_mutex)
                {
                    _shuttingDown = true;
                    _sendSemaphore.Complete(exception);
                }
            }
            else
            {
                bool alreadyShuttingDown = false;
                lock (_mutex)
                {
                    if (_shuttingDown)
                    {
                        alreadyShuttingDown = true;
                    }
                    else
                    {
                        _shuttingDown = true;
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
                    EncodeCloseConnectionFrame(_networkConnectionWriter);

                    // The flush can't be canceled because it would lead to the writing of an incomplete frame.
                    await _networkConnectionWriter.FlushAsync(CancellationToken.None).ConfigureAwait(false);
                }

                // When the peer receives the CloseConnection frame, the peer closes the connection. We wait for the
                // connection closure here. We can't just return and close the underlying transport since this could
                // abort the receive of the dispatch responses and close connection frame by the peer.
                await _pendingClose.Task.ConfigureAwait(false);
            }

            static void EncodeCloseConnectionFrame(SimpleNetworkConnectionWriter writer)
            {
                var encoder = new SliceEncoder(writer, Encoding.Slice11);
                IceDefinitions.CloseConnectionFrame.Encode(ref encoder);
            }
        }

        internal IceProtocolConnection(
            ISimpleNetworkConnection simpleNetworkConnection,
            Configure.IceProtocolOptions options,
            bool isUdp)
        {
            _isUdp = isUdp;
            _options = options;

            // TODO: get the pool and minimum segment size from an option class, but which one? The Slic connection
            // gets these from SlicOptions but another option could be to add Pool/MinimunSegmentSize on
            // ConnectionOptions/ServerOptions. These properties would be used by:
            // - the multiplexed transport implementations
            // - the Ice protocol connection
            _memoryPool = MemoryPool<byte>.Shared;
            _minimumSegmentSize = 4096;

            _networkConnectionWriter = new SimpleNetworkConnectionWriter(
                simpleNetworkConnection,
                _memoryPool,
                _minimumSegmentSize);
            _networkConnectionReader = new SimpleNetworkConnectionReader(
                simpleNetworkConnection,
                _memoryPool,
                _minimumSegmentSize);

            _payloadWriter = new IcePayloadPipeWriter(_networkConnectionWriter);
        }

        internal async Task InitializeAsync(bool isServer, CancellationToken cancel)
        {
            if (!_isUdp)
            {
                if (isServer)
                {
                    EncodeValidateConnectionFrame(_networkConnectionWriter);

                    // The flush can't be canceled because it would lead to the writing of an incomplete frame.
                    await _networkConnectionWriter.FlushAsync(CancellationToken.None).ConfigureAwait(false);
                }
                else
                {
                    ReadOnlySequence<byte> buffer = await _networkConnectionReader.ReadAtLeastAsync(
                        IceDefinitions.PrologueSize,
                        cancel).ConfigureAwait(false);

                    (IcePrologue validateConnectionFrame, long consumed) = DecodeValidateConnectionFrame(buffer);
                    _networkConnectionReader.AdvanceTo(buffer.GetPosition(consumed), buffer.End);

                    IceDefinitions.CheckPrologue(validateConnectionFrame);
                    if (validateConnectionFrame.FrameSize != IceDefinitions.PrologueSize)
                    {
                        throw new InvalidDataException(
                            $"received Ice frame with only '{validateConnectionFrame.FrameSize}' bytes");
                    }
                    if (validateConnectionFrame.FrameType != IceFrameType.ValidateConnection)
                    {
                        throw new InvalidDataException(@$"expected '{nameof(IceFrameType.ValidateConnection)
                            }' frame but received frame type '{validateConnectionFrame.FrameType}'");
                    }
                }
            }

            static void EncodeValidateConnectionFrame(SimpleNetworkConnectionWriter writer)
            {
                var encoder = new SliceEncoder(writer, Encoding.Slice11);
                IceDefinitions.ValidateConnectionFrame.Encode(ref encoder);
            }

            static (IcePrologue, long) DecodeValidateConnectionFrame(ReadOnlySequence<byte> buffer)
            {
                var decoder = new SliceDecoder(buffer, Encoding.Slice11);
                return (new IcePrologue(ref decoder), decoder.Consumed);
            }
        }

        private void CancelDispatches()
        {
            IEnumerable<CancellationTokenSource> dispatches;
            lock (_mutex)
            {
                dispatches = _dispatches.ToArray();
            }

            foreach (CancellationTokenSource cancelDispatchSource in dispatches)
            {
                try
                {
                    cancelDispatchSource.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // Ignore, the dispatch completed concurrently.
                }
            }
        }

        private void CancelInvocations(Exception exception)
        {
            IEnumerable<TaskCompletionSource<PipeReader>> invocations;
            lock (_mutex)
            {
                invocations = _invocations.Values.ToArray();
            }

            foreach (TaskCompletionSource<PipeReader> responseCompletionSource in invocations)
            {
                responseCompletionSource.TrySetException(exception);
            }
        }

        /// <summary>Creates a pipe reader to simplify the reading of a request or response frame. The frame is read
        /// fully and buffered into an internal pipe.</summary>
        private static async ValueTask<PipeReader> CreateFrameReaderAsync(
            int size,
            SimpleNetworkConnectionReader networkConnectionReader,
            MemoryPool<byte> pool,
            int minimumSegmentSize,
            CancellationToken cancel)
        {
            var pipe = new Pipe(new PipeOptions(
                pool: pool,
                minimumSegmentSize: minimumSegmentSize,
                pauseWriterThreshold: 0,
                writerScheduler: PipeScheduler.Inline));

            try
            {
                await networkConnectionReader.FillBufferWriterAsync(
                    pipe.Writer,
                    size,
                    cancel).ConfigureAwait(false);
            }
            catch
            {
                await pipe.Reader.CompleteAsync().ConfigureAwait(false);
                throw;
            }
            finally
            {
                await pipe.Writer.CompleteAsync().ConfigureAwait(false);
            }

            return pipe.Reader;
        }

        /// <summary>Reads the full Ice payload from the given payload source.</summary>
        private static async ValueTask<ReadOnlySequence<byte>> ReadFullPayloadAsync(
            PipeReader payloadSource,
            CancellationToken cancel)
        {
            // We use ReadAtLeastAsync instead of ReadAsync to bypass the PauseWriterThreshold when the payloadSource
            // is backed by a Pipe.
            ReadResult readResult = await payloadSource.ReadAtLeastAsync(int.MaxValue, cancel).ConfigureAwait(false);

            if (readResult.IsCanceled)
            {
                throw new OperationCanceledException();
            }

            return readResult.IsCompleted ? readResult.Buffer :
                throw new ArgumentException("the payload size is greater than int.MaxValue", nameof(payloadSource));
        }

        /// <summary>Receives incoming frames and returns once a request frame is received.</summary>
        /// <returns>The size of the request frame.</returns>
        /// <remarks>When this method returns, only the frame prologue has been read from the network. The caller is
        /// responsible to read the remainder of the request frame from _networkConnectionReader.</remarks>
        private async ValueTask<int> ReceiveFrameAsync()
        {
            // Reads are not cancelable. This method returns once a request frame is read or when the connection is
            // disposed.
            CancellationToken cancel = CancellationToken.None;

            while (true)
            {
                ReadOnlySequence<byte> buffer = await _networkConnectionReader.ReadAtLeastAsync(
                    IceDefinitions.PrologueSize,
                    cancel).ConfigureAwait(false);

                // First decode and check the prologue.

                ReadOnlySequence<byte> prologueBuffer = buffer.Slice(0, IceDefinitions.PrologueSize);

                IcePrologue prologue = Encoding.Slice11.DecodeBuffer(
                    prologueBuffer,
                    (ref SliceDecoder decoder) => new IcePrologue(ref decoder));

                _networkConnectionReader.AdvanceTo(prologueBuffer.End);

                IceDefinitions.CheckPrologue(prologue);
                if (prologue.FrameSize > _options.MaxIncomingFrameSize)
                {
                    throw new InvalidDataException(
                        $"incoming frame size ({prologue.FrameSize}) is greater than max incoming frame size");
                }
                else if (_isUdp && (prologue.FrameSize > buffer.Length || prologue.FrameSize > UdpUtils.MaxPacketSize))
                {
                    // Ignore truncated UDP datagram.
                    continue; // while
                }

                if (prologue.CompressionStatus == 2)
                {
                    throw new NotSupportedException("cannot decompress Ice frame");
                }

                // Then process the frame based on its type.

                switch (prologue.FrameType)
                {
                    case IceFrameType.CloseConnection:
                    {
                        if (prologue.FrameSize != IceDefinitions.PrologueSize)
                        {
                            throw new InvalidDataException(
                                $"unexpected data for {nameof(IceFrameType.CloseConnection)}");
                        }
                        if (_isUdp)
                        {
                            throw new InvalidDataException(
                                $"unexpected {nameof(IceFrameType.CloseConnection)} frame for udp connection");
                        }

                        lock (_mutex)
                        {
                            // If local shutdown is in progress, shutdown from peer prevails. The local shutdown
                            // will return once the connection disposes this protocol connection.
                            _shuttingDown = true;
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

                    case IceFrameType.Request:
                        // the caller will read the request from _networkConnectionReader
                        return prologue.FrameSize;

                    case IceFrameType.RequestBatch:
                        // Read and ignore
                        PipeReader batchRequestReader = await CreateFrameReaderAsync(
                            prologue.FrameSize - IceDefinitions.PrologueSize,
                            _networkConnectionReader,
                            _memoryPool,
                            _minimumSegmentSize,
                            CancellationToken.None).ConfigureAwait(false);
                        await batchRequestReader.CompleteAsync().ConfigureAwait(false);
                        break;

                    case IceFrameType.Reply:
                        // Read the remainder of the frame immediately into frameReader.
                        PipeReader replyFrameReader = await CreateFrameReaderAsync(
                            prologue.FrameSize - IceDefinitions.PrologueSize,
                            _networkConnectionReader,
                            _memoryPool,
                            _minimumSegmentSize,
                            CancellationToken.None).ConfigureAwait(false);

                        bool cleanupFrameReader = true;

                        try
                        {
                            // Read and decode request ID
                            if (!replyFrameReader.TryRead(out ReadResult readResult) || readResult.Buffer.Length < 4)
                            {
                                throw new ConnectionLostException();
                            }

                            ReadOnlySequence<byte> requestIdBuffer = readResult.Buffer.Slice(0, 4);
                            int requestId = Encoding.Slice11.DecodeBuffer(
                                requestIdBuffer,
                                (ref SliceDecoder decoder) => decoder.DecodeInt());
                            replyFrameReader.AdvanceTo(requestIdBuffer.End);

                            lock (_mutex)
                            {
                                if (_invocations.TryGetValue(
                                    requestId,
                                    out TaskCompletionSource<PipeReader>? responseCompletionSource))
                                {
                                    responseCompletionSource.SetResult(replyFrameReader);

                                    cleanupFrameReader = false;
                                }
                                else if (!_shuttingDown)
                                {
                                    throw new InvalidDataException("received ice Reply for unknown invocation");
                                }
                            }
                        }
                        finally
                        {
                            if (cleanupFrameReader)
                            {
                                await replyFrameReader.CompleteAsync().ConfigureAwait(false);
                            }
                        }
                        break;

                    case IceFrameType.ValidateConnection:
                    {
                        // Notify the control stream of the reception of a Ping frame.
                        if (prologue.FrameSize != IceDefinitions.PrologueSize)
                        {
                            throw new InvalidDataException(
                                $"unexpected data for {nameof(IceFrameType.ValidateConnection)}");
                        }
                        break;
                    }

                    default:
                    {
                        throw new InvalidDataException(
                            $"received Ice frame with unknown frame type '{prologue.FrameType}'");
                    }
                }
            } // while
        }
    }
}
