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
        public event Action<string>? PeerShutdownInitiated;

        private static readonly IDictionary<int, ReadOnlySequence<byte>> _idempotentFields =
            new Dictionary<int, ReadOnlySequence<byte>> { [(int)FieldKey.Idempotent] = default }.ToImmutableDictionary();

        private readonly TaskCompletionSource _dispatchesAndInvocationsCompleted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly HashSet<IncomingRequest> _dispatches = new();
        private readonly int _incomingFrameMaxSize;
        private readonly Dictionary<int, OutgoingRequest> _invocations = new();
        private readonly bool _isUdp;

        private readonly MemoryPool<byte> _memoryPool;
        private readonly int _minimumSegmentSize;

        private readonly object _mutex = new();

        private readonly ISimpleNetworkConnection _networkConnection;
        private readonly SimpleNetworkConnectionPipeWriter _networkConnectionWriter;
        private readonly SimpleNetworkConnectionPipeReader _networkConnectionReader;
        private int _nextRequestId;
        private readonly TaskCompletionSource _pendingClose = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly AsyncSemaphore _sendSemaphore = new(1, 1);
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
                EncodeValidateConnectionFrame(_networkConnectionWriter);
                await _networkConnectionWriter.FlushAsync(cancel).ConfigureAwait(false);
            }
            finally
            {
                _sendSemaphore.Release();
            }

            static void EncodeValidateConnectionFrame(PipeWriter writer)
            {
                var encoder = new SliceEncoder(writer, Encoding.Slice11);
                IceDefinitions.ValidateConnectionFrame.Encode(ref encoder);
            }
        }

        /// <inheritdoc/>
        public async Task<IncomingRequest> ReceiveRequestAsync()
        {
            while (true)
            {
                // Wait for a request frame to be received.
                int requestId;
                int frameSize;
                try
                {
                    (requestId, frameSize) = await ReceiveFrameAsync().ConfigureAwait(false);
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

                // TODO: Add TryDecodeHeader to avoid reading the whole frame here?
                ReadResult result = await _networkConnectionReader.ReadAtLeastAsync(
                    frameSize,
                    CancellationToken.None).ConfigureAwait(false);

                (IceRequestHeader requestHeader, long consumed) = DecodeHeader(result.Buffer.Slice(0, frameSize));
                _networkConnectionReader.AdvanceTo(result.Buffer.GetPosition(consumed));

                // The payload size is the encapsulation size less the 6 bytes of the encapsulation header.
                int payloadSize = requestHeader.EncapsulationHeader.EncapsulationSize - 6;

                var payloadEncoding = Encoding.FromMajorMinor(
                    requestHeader.EncapsulationHeader.PayloadEncodingMajor,
                    requestHeader.EncapsulationHeader.PayloadEncodingMinor);

                var payloadReader = new IcePayloadPipeReader(
                    _networkConnectionReader,
                    payloadSize,
                    _memoryPool,
                    _minimumSegmentSize);

                // TODO: fetches the full payload now. Is this the best behavior however? Another option is to not fetch
                // the full payload (requires to the not read the whole frame above). The connection dispatcher is
                // responsible for reading the full payload. With this behavior, request dispatch would be serialized
                // and reading of request/response would only be resumed once the full payload is read. This allows to
                // implement flow control at the middleware level for Ice connections (a middleware can read the full
                // payload to allow other requests to be dispatched). See also the following issues:
                // https://github.com/zeroc-ice/icerpc-csharp/issues/583
                // https://github.com/zeroc-ice/icerpc-csharp/issues/661
                // https://github.com/zeroc-ice/icerpc-csharp/issues/684
                await payloadReader.FetchFullPayloadAsync(CancellationToken.None).ConfigureAwait(false);

                var request = new IncomingRequest(
                    Protocol.Ice,
                    path: requestHeader.Path,
                    fragment: requestHeader.Fragment,
                    operation: requestHeader.Operation,
                    payload: payloadReader,
                    payloadEncoding,
                    responseWriter: _networkConnectionWriter)
                {
                    Fields = requestHeader.OperationMode == OperationMode.Normal ?
                        ImmutableDictionary<int, ReadOnlySequence<byte>>.Empty : _idempotentFields,
                    IsOneway = requestId == 0,
                };

                request.Features = request.Features.With(new IceRequest(requestId, outgoing: false));
                if (requestHeader.Context.Count > 0)
                {
                    request.Features = request.Features.WithContext(requestHeader.Context);
                }

                lock (_mutex)
                {
                    if (!_shutdown)
                    {
                        _dispatches.Add(request);
                        request.CancelDispatchSource = new();
                        return request;
                    }
                }

                // If shutting down, ignore the incoming request and continue receiving frames until the connection is
                // closed.
                await request.CompleteAsync(new ConnectionClosedException()).ConfigureAwait(false);
            }

            static (IceRequestHeader, long) DecodeHeader(ReadOnlySequence<byte> buffer)
            {
                var decoder = new SliceDecoder(buffer, Encoding.Slice11);
                return (new IceRequestHeader(ref decoder), decoder.Consumed);
            }
        }

        /// <inheritdoc/>
        public async Task<IncomingResponse> ReceiveResponseAsync(OutgoingRequest request, CancellationToken cancel)
        {
            // This class sent this request and didn't set a ResponseReader on it.
            Debug.Assert(request.ResponseReader == null);
            Debug.Assert(!request.IsOneway);

            IceRequest? requestFeature = request.Features.Get<IceRequest>();
            if (requestFeature == null || requestFeature.ResponseCompletionSource == null)
            {
                throw new InvalidOperationException("unknown request");
            }

            // Wait for the response.
            int frameSize;
            try
            {
                frameSize = await requestFeature.ResponseCompletionSource.Task.WaitAsync(cancel).ConfigureAwait(false);
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

            ReadResult result = await _networkConnectionReader.ReadAtLeastAsync(
                frameSize,
                CancellationToken.None).ConfigureAwait(false);

            // TODO: Add TryDecodeHeader to avoid reading the whole frame here?
            (ReplyStatus replyStatus, int payloadSize, long consumed) = DecodeHeader(result.Buffer.Slice(0, frameSize));
            _networkConnectionReader.AdvanceTo(result.Buffer.GetPosition(consumed));

            ResultType resultType = replyStatus switch
            {
                ReplyStatus.OK => ResultType.Success,
                ReplyStatus.UserException => (ResultType)SliceResultType.ServiceFailure,
                _ => ResultType.Failure
            };

            // For compatibility with ZeroC Ice "indirect" proxies
            if (replyStatus == ReplyStatus.ObjectNotExistException && request.Proxy.Endpoint == null)
            {
                request.Features = request.Features.With(RetryPolicy.OtherReplica);
            }

            // TODO: see comments from ReceiveRequestAsync
            var payloadReader = new IcePayloadPipeReader(
                _networkConnectionReader,
                payloadSize,
                MemoryPool<byte>.Shared,
                4096);
            await payloadReader.FetchFullPayloadAsync(CancellationToken.None).ConfigureAwait(false);

            return new IncomingResponse(
                request,
                resultType,
                payloadReader);

            static (ReplyStatus ReplyStatus, int PayloadSize, int Consumed) DecodeHeader(ReadOnlySequence<byte> buffer)
            {
                // Decode the response.
                var decoder = new SliceDecoder(buffer, Encoding.Slice11);

                ReplyStatus replyStatus = decoder.DecodeReplyStatus();

                int payloadSize;

                if (replyStatus <= ReplyStatus.UserException)
                {
                    var encapsulationHeader = new EncapsulationHeader(ref decoder);
                    payloadSize = encapsulationHeader.EncapsulationSize - 6;

                    // we ignore the payload encoding, it's irrelevant: the caller knows which encoding to expect,
                    // usually the same encoding as the request payload.

                    if (payloadSize != buffer.Length - 7)
                    {
                        throw new InvalidDataException(@$"response payload size mismatch: expected {payloadSize
                            } bytes, read {buffer.Length - 7} bytes");
                    }
                }
                else
                {
                    // Ice system exception
                    payloadSize = (int)buffer.Length - 1; // includes reply status
                }

                return (replyStatus, payloadSize, (int)decoder.Consumed);
            }
        }

        /// <inheritdoc/>
        public async Task SendRequestAsync(OutgoingRequest request, CancellationToken cancel)
        {
            if (_isUdp && !request.IsOneway)
            {
                throw new InvalidOperationException("cannot send twoway request over UDP");
            }

            // Wait for sending of other frames to complete. The semaphore is used as an asynchronous queue to serialize
            // the sending of frames.
            await _sendSemaphore.EnterAsync(cancel).ConfigureAwait(false);

            // Assign the request ID for twoway invocations and keep track of the invocation for receiving the response.
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
                        request.Features = request.Features.With(new IceRequest(requestId, outgoing: true));
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
                (int payloadSize, bool isCanceled, bool isCompleted) =
                    await request.PayloadSource.DecodeSegmentSizeAsync(cancel).ConfigureAwait(false);

                if (isCanceled)
                {
                    throw new OperationCanceledException();
                }

                if (payloadSize > 0 && isCompleted)
                {
                    throw new ArgumentException(
                        $"expected {payloadSize} bytes in request payload source, but it's empty");
                }

                // If the application sets the payload sink, the initial payload sink is set and we need to set the
                // stream output on the delayed pipe writer decorator. Otherwise, we directly use the stream output.
                PipeWriter payloadSink;
                if (request.InitialPayloadSink == null)
                {
                    payloadSink = _networkConnectionWriter;
                }
                else
                {
                    request.InitialPayloadSink.SetDecoratee(_networkConnectionWriter);
                    payloadSink = request.PayloadSink;
                }

                EncodeHeader(_networkConnectionWriter, payloadSize);

                await SendPayloadAsync(request, payloadSink, cancel).ConfigureAwait(false);
                request.IsSent = true;
            }
            catch (ObjectDisposedException exception)
            {
                // If the network connection has been disposed, we raise ConnectionLostException to ensure the
                // request is retried by the retry interceptor.
                // TODO: this is clunky but required for retries to work because the retry interceptor only retries
                // a request if the exception is a transport exception.
                var ex = new ConnectionLostException(exception);
                await request.CompleteAsync(ex).ConfigureAwait(false);
                throw ex;
            }
            catch (Exception ex)
            {
                await request.CompleteAsync(ex).ConfigureAwait(false);
                throw;
            }
            finally
            {
                _sendSemaphore.Release();
            }

            void EncodeHeader(PipeWriter output, int payloadSize)
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
                    // We're not checking FieldsOverrides because it makes no sense to use FieldsOverrides for
                    // idempotent.
                    request.Fields.ContainsKey((int)FieldKey.Idempotent) ? OperationMode.Idempotent :
                        OperationMode.Normal,
                    request.Features.GetContext(),
                    new EncapsulationHeader(encapsulationSize: payloadSize + 6, encodingMajor, encodingMinor));
                requestHeader.Encode(ref encoder);

                SliceEncoder.EncodeInt(encoder.EncodedByteCount + payloadSize, sizePlaceholder.Span);
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

            if (request.IsOneway)
            {
                await response.CompleteAsync().ConfigureAwait(false);
                return;
            }

            // Wait for sending of other frames to complete. The semaphore is used as an asynchronous
            // queue to serialize the sending of frames.
            await _sendSemaphore.EnterAsync(cancel).ConfigureAwait(false);

            try
            {
                Debug.Assert(!_isUdp); // udp is oneway-only so no response

                (int payloadSize, bool isCanceled, bool isCompleted) =
                    await response.PayloadSource.DecodeSegmentSizeAsync(cancel).ConfigureAwait(false);

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

                if (response.ResultType != ResultType.Success)
                {
                    if (response.ResultType == ResultType.Failure)
                    {
                        // extract reply status from 1.1-encoded payload
                        ReadResult readResult = await response.PayloadSource.ReadAsync(cancel).ConfigureAwait(false);

                        if (readResult.IsCanceled)
                        {
                            throw new OperationCanceledException();
                        }
                        if (readResult.Buffer.IsEmpty)
                        {
                            throw new ArgumentException("empty exception payload");
                        }

                        replyStatus = (ReplyStatus)readResult.Buffer.FirstSpan[0];

                        if (replyStatus <= ReplyStatus.UserException)
                        {
                            throw new InvalidDataException("unexpected reply status value '{replyStatus}' in payload");
                        }

                        response.PayloadSource.AdvanceTo(readResult.Buffer.GetPosition(1));
                        payloadSize -= 1;
                    }
                    else
                    {
                        replyStatus = ReplyStatus.UserException;
                    }
                }

                EncodeHeader(_networkConnectionWriter, payloadSize, replyStatus);
                await SendPayloadAsync(response, response.PayloadSink, cancel).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await response.CompleteAsync(ex).ConfigureAwait(false);
                throw;
            }
            finally
            {
                _sendSemaphore.Release();

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

            void EncodeHeader(PipeWriter writer, int payloadSize, ReplyStatus replyStatus)
            {
                var encoder = new SliceEncoder(writer, Encoding.Slice11);

                // Write the response header.

                encoder.WriteByteSpan(IceDefinitions.FramePrologue);
                encoder.EncodeIceFrameType(IceFrameType.Reply);
                encoder.EncodeByte(0); // compression status
                Memory<byte> sizePlaceholder = encoder.GetPlaceholderMemory(4);

                encoder.EncodeInt(requestId);

                encoder.EncodeReplyStatus(replyStatus);
                if (replyStatus <= ReplyStatus.UserException)
                {
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

                SliceEncoder.EncodeInt(encoder.EncodedByteCount + payloadSize, sizePlaceholder.Span);
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
                    EncodeCloseConnectionFrame(_networkConnectionWriter);
                    await _networkConnectionWriter.FlushAsync(CancellationToken.None).ConfigureAwait(false);
                }

                // When the peer receives the CloseConnection frame, the peer closes the connection. We wait for the
                // connection closure here. We can't just return and close the underlying transport since this could
                // abort the receive of the dispatch responses and close connection frame by the peer.
                await _pendingClose.Task.ConfigureAwait(false);
            }

            static void EncodeCloseConnectionFrame(PipeWriter writer)
            {
                var encoder = new SliceEncoder(writer, Encoding.Slice11);
                IceDefinitions.CloseConnectionFrame.Encode(ref encoder);
            }
        }

        internal IceProtocolConnection(
            ISimpleNetworkConnection simpleNetworkConnection,
            int incomingFrameMaxSize,
            bool isUdp)
        {
            _isUdp = isUdp;
            _incomingFrameMaxSize = incomingFrameMaxSize;

            // TODO: get the pool and minimum segment size from an option class, but which one? The Slic connection
            // gets these from SlicOptions but another option could to add Pool/MinimunSegmentSize on
            // ConnectionOptions/ServerOptions. These properties would be used by:
            // - the multiplexed transport implementations
            // - the Ice protocol connection
            _memoryPool = MemoryPool<byte>.Shared;
            _minimumSegmentSize = 4096;

            _networkConnection = simpleNetworkConnection;
            _networkConnectionWriter = new SimpleNetworkConnectionPipeWriter(
                simpleNetworkConnection,
                _memoryPool,
                _minimumSegmentSize);
            _networkConnectionReader = new SimpleNetworkConnectionPipeReader(
                simpleNetworkConnection,
                _memoryPool,
                _minimumSegmentSize);
        }

        internal async Task InitializeAsync(bool isServer, CancellationToken cancel)
        {
            if (!_isUdp)
            {
                if (isServer)
                {
                    EncodeValidateConnectionFrame(_networkConnectionWriter);
                    await _networkConnectionWriter.FlushAsync(cancel).ConfigureAwait(false);
                }
                else
                {
                    ReadResult result = await _networkConnectionReader.ReadAtLeastAsync(
                        IceDefinitions.PrologueSize,
                        cancel).ConfigureAwait(false);

                    (IcePrologue validateConnectionFrame, long consumed) = DecodeValidateConnectionFrame(result.Buffer);
                    _networkConnectionReader.AdvanceTo(result.Buffer.GetPosition(consumed));

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

            static void EncodeValidateConnectionFrame(PipeWriter writer)
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

        /// <summary>Sends the payload source of an outgoing frame.</summary>
        private static async ValueTask SendPayloadAsync(
            OutgoingFrame outgoingFrame,
            PipeWriter payloadSink,
            CancellationToken cancel)
        {
            if (outgoingFrame.PayloadSourceStream != null)
            {
                // Since the payload is encoded with a Slice encoding, PayloadSourceStream can only come from
                // a Slice stream parameter/return.
                throw new NotSupportedException("stream parameters and return values are not supported with ice");
            }

            FlushResult flushResult = await payloadSink.CopyFromAsync(
                outgoingFrame.PayloadSource,
                completeWhenDone: true,
                cancel).ConfigureAwait(false);

            Debug.Assert(!flushResult.IsCanceled); // not implemented

            if (flushResult.IsCompleted)
            {
                // The remote reader gracefully complete the stream input pipe. TODO: which exception should we
                // throw here? We throw OperationCanceledException... which implies that if the frame is an outgoing
                // request is won't be retried.
                throw new OperationCanceledException("peer stopped reading the payload");
            }

            await outgoingFrame.CompleteAsync().ConfigureAwait(false);
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
                request.Features.Get<IceRequest>()!.ResponseCompletionSource!.TrySetException(exception);
            }
        }

        private async ValueTask<(int RequestId, int FrameSize)> ReceiveFrameAsync()
        {
            // Reads are not cancellable. This method returns once a frame is read or when the connection is disposed.
            CancellationToken cancel = CancellationToken.None;

            while (true)
            {
                ReadResult result = await _networkConnectionReader.ReadAtLeastAsync(
                    IceDefinitions.PrologueSize,
                    cancel).ConfigureAwait(false);

                // Decode the prologue and eventually the request ID.
                (IcePrologue prologue, int? requestId, long consumed) = DecodePrologue(result.Buffer);

                _networkConnectionReader.AdvanceTo(result.Buffer.GetPosition(IceDefinitions.PrologueSize));

                // Check the header
                IceDefinitions.CheckPrologue(prologue);
                if (_isUdp && prologue.FrameSize > result.Buffer.Length)
                {
                    // Ignore truncated UDP datagram.
                    continue; // while
                }
                else if (prologue.FrameSize > _incomingFrameMaxSize)
                {
                    if (_isUdp)
                    {
                        continue;
                    }
                    else
                    {
                        throw new InvalidDataException(@$"frame with {
                            prologue.FrameSize} bytes exceeds IncomingFrameMaxSize connection option value");
                    }
                }

                if (prologue.CompressionStatus == 2)
                {
                    throw new NotSupportedException("cannot decompress Ice frame");
                }

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

                    case IceFrameType.Request:
                    {
                        Debug.Assert(requestId != null);
                        return (requestId.Value, prologue.FrameSize);
                    }

                    case IceFrameType.RequestBatch:
                    {
                        // TODO: skip the data

                        break; // Batch requests are ignored because not supported
                    }

                    case IceFrameType.Reply:
                    {
                        // int requestId = Slice11Encoding.DecodeFixedLengthSize(buffer.Span[0..4]);
                        // TODO: XXX we keep these 4 bytes in buffer

                        lock (_mutex)
                        {
                            Debug.Assert(requestId != null);
                            if (_invocations.TryGetValue(requestId.Value, out OutgoingRequest? request))
                            {
                                request.Features.Get<IceRequest>()!.ResponseCompletionSource!.SetResult(prologue.FrameSize);
                            }
                            else if (!_shutdown)
                            {
                                throw new InvalidDataException("received ice Reply for unknown invocation");
                            }
                        }
                        break;
                    }

                    case IceFrameType.ValidateConnection:
                    {
                        // Notify the control stream of the reception of a Ping frame.
                        if (prologue.FrameSize != IceDefinitions.PrologueSize)
                        {
                            throw new InvalidDataException(
                                $"unexpected data for {nameof(IceFrameType.ValidateConnection)}");
                        }
                        // TODO: implement protocol logging with decorators
                        // _logger.LogReceivedIceValidateConnectionFrame();
                        break;
                    }

                    default:
                    {
                        throw new InvalidDataException(
                            $"received Ice frame with unknown frame type '{prologue.FrameType}'");
                    }
                }
            } // while

            static (IcePrologue, int?, long) DecodePrologue(ReadOnlySequence<byte> buffer)
            {
                var decoder = new SliceDecoder(buffer, Encoding.Slice11);
                var prologue = new IcePrologue(ref decoder);
                int? requestId = null;
                if (prologue.FrameType == IceFrameType.Reply || prologue.FrameType == IceFrameType.Request)
                {
                    requestId = decoder.DecodeInt();
                }
                return (prologue, requestId, decoder.Consumed);
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
