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
        private readonly HashSet<IncomingRequest> _dispatches = new();
        private readonly Dictionary<int, OutgoingRequest> _invocations = new();
        private readonly bool _isUdp;

        private readonly MemoryPool<byte> _memoryPool;
        private readonly int _minimumSegmentSize;

        private readonly object _mutex = new();

        private readonly ISimpleNetworkConnection _networkConnection;
        private readonly SimpleNetworkConnectionPipeWriter _networkConnectionWriter;
        private readonly SimpleNetworkConnectionPipeReader _networkConnectionReader;
        private int _nextRequestId;
        private readonly IcePayloadPipeWriter _payloadWriter;
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
                IceRequestHeader requestHeader;
                PipeReader payloadReader;
                try
                {
                    (requestId, requestHeader, payloadReader) = await ReceiveFrameAsync().ConfigureAwait(false);
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

                var request = new IncomingRequest(Protocol.Ice)
                {
                    Fields = requestHeader.OperationMode == OperationMode.Normal ?
                            ImmutableDictionary<RequestFieldKey, ReadOnlySequence<byte>>.Empty : _idempotentFields,
                    Fragment = requestHeader.Fragment,
                    IsOneway = requestId == 0,
                    Operation = requestHeader.Operation,
                    Path = requestHeader.Path,
                    Payload = payloadReader,
                    PayloadEncoding = Encoding.FromMajorMinor(
                        requestHeader.EncapsulationHeader.PayloadEncodingMajor,
                        requestHeader.EncapsulationHeader.PayloadEncodingMinor),
                    ResponseWriter = _payloadWriter,
                };

                if (requestId > 0)
                {
                    request.Features = request.Features.With(new IceIncomingRequest(requestId));
                }

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
                await request.Payload.CompleteAsync(new ConnectionClosedException()).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async Task<IncomingResponse> ReceiveResponseAsync(OutgoingRequest request, CancellationToken cancel)
        {
            // This class sent this request and didn't set a ResponseReader on it.
            Debug.Assert(request.ResponseReader == null);
            Debug.Assert(!request.IsOneway);

            if (request.Features.Get<IceOutgoingRequest>() is not IceOutgoingRequest requestFeature)
            {
                throw new InvalidOperationException("unknown request");
            }

            // Wait for the response.
            try
            {
                return await requestFeature.IncomingResponseCompletionSource.Task.WaitAsync(
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
        }

        /// <inheritdoc/>
        public async Task SendRequestAsync(OutgoingRequest request, CancellationToken cancel)
        {
            if (_isUdp && !request.IsOneway)
            {
                await CompleteRequestAsync().ConfigureAwait(false);
                throw new InvalidOperationException("cannot send twoway request over UDP");
            }

            // Set the final payload sink to the stateless payload writer.
            request.SetFinalPayloadSink(_payloadWriter);

            int requestId = 0;
            try
            {
                // Wait for sending of other frames to complete. The semaphore is used as an asynchronous queue to serialize
                // the sending of frames.
                await _sendSemaphore.EnterAsync(cancel).ConfigureAwait(false);

                // Assign the request ID for twoway invocations and keep track of the invocation for receiving the response.
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
                            request.Features = request.Features.With(new IceOutgoingRequest(requestId));
                        }
                    }
                    catch
                    {
                        _sendSemaphore.Release();
                        throw;
                    }
                }
            }
            catch (Exception exception)
            {
                await CompleteRequestAsync(exception).ConfigureAwait(false);
                throw;
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

                EncodeHeader(_networkConnectionWriter, payloadSize);
                await SendPayloadAsync(request, cancel).ConfigureAwait(false);
                request.IsSent = true;

                // SendPayloadSink takes care of the completion of the payload sink / sources.
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
                await CompleteRequestAsync(exception).ConfigureAwait(false);
                throw;
            }
            finally
            {
                _sendSemaphore.Release();
            }

            async Task CompleteRequestAsync(Exception? exception = null)
            {
                await request.PayloadSink.CompleteAsync(exception).ConfigureAwait(false);
                await request.PayloadSource.CompleteAsync(exception).ConfigureAwait(false);
                if (request.PayloadSourceStream != null)
                {
                    await request.PayloadSourceStream.CompleteAsync(exception).ConfigureAwait(false);
                }
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
                    request.Fields.ContainsKey(RequestFieldKey.Idempotent) ?
                        OperationMode.Idempotent : OperationMode.Normal,
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
            if (request.IsOneway)
            {
                await CompleteResponseAsync().ConfigureAwait(false);
                if (response.PayloadSourceStream != null)
                {
                    // Since the payload is encoded with a Slice encoding, PayloadSourceStream can only come from a
                    // Slice stream parameter/return.
                    throw new NotSupportedException("stream parameters and return values are not supported with ice");
                }
                return;
            }

            IceIncomingRequest? requestFeature = request.Features.Get<IceIncomingRequest>();
            try
            {
                if (requestFeature == null)
                {
                    throw new InvalidOperationException("request ID feature is not set");
                }

                // Wait for sending of other frames to complete. The semaphore is used as an asynchronous queue to
                // serialize the sending of frames.
                await _sendSemaphore.EnterAsync(cancel).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                await CompleteResponseAsync(exception).ConfigureAwait(false);
            }

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
                await SendPayloadAsync(response, cancel).ConfigureAwait(false);

                // SendPayloadSink takes care of the completion of the payload sink / sources.
            }
            catch (Exception exception)
            {
                await CompleteResponseAsync(exception).ConfigureAwait(false);
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

            async Task CompleteResponseAsync(Exception? exception = null)
            {
                await response.PayloadSink.CompleteAsync(exception).ConfigureAwait(false);
                await response.PayloadSource.CompleteAsync(exception).ConfigureAwait(false);
                if (response.PayloadSourceStream != null)
                {
                    await response.PayloadSourceStream.CompleteAsync(exception).ConfigureAwait(false);
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

                encoder.EncodeInt(requestFeature!.Id);

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

        internal IceProtocolConnection(ISimpleNetworkConnection simpleNetworkConnection, bool isUdp)
        {
            _isUdp = isUdp;

            // TODO: get the pool and minimum segment size from an option class, but which one? The Slic connection
            // gets these from SlicOptions but another option could be to add Pool/MinimunSegmentSize on
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

            _payloadWriter = new IcePayloadPipeWriter(_networkConnectionWriter);
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
                    _networkConnectionReader.AdvanceTo(result.Buffer.GetPosition(consumed), result.Buffer.End);

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

        /// <summary>Sends the payload source and payload source stream of an outgoing frame. The payload source and
        /// sink are completed if successful.</summary>
        private static async ValueTask SendPayloadAsync(OutgoingFrame outgoingFrame, CancellationToken cancel)
        {
            if (outgoingFrame.PayloadSourceStream != null)
            {
                // Since the payload is encoded with a Slice encoding, PayloadSourceStream can only come from a Slice
                // stream parameter/return.
                throw new NotSupportedException("stream parameters and return values are not supported with ice");
            }

            FlushResult flushResult = await outgoingFrame.PayloadSink.CopyFromAsync(
                outgoingFrame.PayloadSource,
                endStream: true,
                cancel).ConfigureAwait(false);

            // If a payload source sink decorator returns a canceled or completed flush result, we have to raise
            // NotSupportedException. We can't interrupt the sending of a payload since it would lead to a bogus payload
            // to be sent over the connection.
            if (flushResult.IsCanceled || flushResult.IsCompleted)
            {
                throw new NotSupportedException("payload sink cancellation or completion is not supported");
            }

            await outgoingFrame.PayloadSource.CompleteAsync().ConfigureAwait(false);
            await outgoingFrame.PayloadSink.CompleteAsync().ConfigureAwait(false);
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
                request.Features.Get<IceOutgoingRequest>()!.IncomingResponseCompletionSource.TrySetException(exception);
            }
        }

        private async ValueTask<(int RequestId, IceRequestHeader RequestHeader, PipeReader PayloadReader)> ReceiveFrameAsync()
        {
            // Reads are not cancelable. This method returns once a frame is read or when the connection is disposed.
            CancellationToken cancel = CancellationToken.None;

            while (true)
            {
                ReadResult result = await _networkConnectionReader.ReadAtLeastAsync(
                    IceDefinitions.PrologueSize,
                    cancel).ConfigureAwait(false);

                // Decode the prologue and eventually the request ID (depending on the frame type and if there's enough
                // data in the buffer to read the request ID).
                (IcePrologue prologue, int? requestId, int consumed) = DecodePrologue(result.Buffer);
                _networkConnectionReader.AdvanceTo(result.Buffer.GetPosition(consumed));

                if (requestId == null &&
                    (prologue.FrameType == IceFrameType.Reply || prologue.FrameType == IceFrameType.Request))
                {
                    result = await _networkConnectionReader.ReadAtLeastAsync(4, cancel).ConfigureAwait(false);
                    requestId = Encoding.Slice11.DecodeBuffer(
                        result.Buffer.Slice(0, 4),
                        (ref SliceDecoder decoder) => decoder.DecodeInt());
                    _networkConnectionReader.AdvanceTo(result.Buffer.GetPosition(4));
                }

                int frameRemainderSize = prologue.FrameSize - consumed;

                // Check the header
                IceDefinitions.CheckPrologue(prologue);
                if (_isUdp &&
                    (prologue.FrameSize > result.Buffer.Length || prologue.FrameSize > UdpUtils.MaxPacketSize))
                {
                    // Ignore truncated UDP datagram.
                    continue; // while
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

                        // Read and decode the remainder of the request frame.
                        result = await _networkConnectionReader.ReadAtLeastAsync(
                            frameRemainderSize,
                            CancellationToken.None).ConfigureAwait(false);

                        ReadOnlySequence<byte> frameData = result.Buffer.Slice(0, frameRemainderSize);
                        (IceRequestHeader requestHeader, consumed) = DecodeRequestHeader(frameData);
                        frameData = frameData.Slice(consumed);

                        var payloadReader = CreateRequestPayloadPipeReader(
                            frameData,
                            _memoryPool,
                            _minimumSegmentSize);

                        _networkConnectionReader.AdvanceTo(result.Buffer.GetPosition(frameRemainderSize));

                        return (requestId.Value, requestHeader, payloadReader);
                    }

                    case IceFrameType.RequestBatch:
                    {
                        // TODO: skip the data

                        break; // Batch requests are ignored because not supported
                    }

                    case IceFrameType.Reply:
                    {
                        Debug.Assert(requestId != null);

                        result = await _networkConnectionReader.ReadAsync(CancellationToken.None).ConfigureAwait(false);

                        if (result.Buffer.IsEmpty)
                        {
                            throw new ConnectionLostException();
                        }

                        int payloadSize;

                        ReplyStatus replyStatus = result.Buffer.FirstSpan[0].AsReplyStatus();

                        if (replyStatus <= ReplyStatus.UserException)
                        {
                            const int headerSize = 7; // reply status byte + encapsulation header

                            // read and check encapsulation header (6 bytes long)
                            if (result.Buffer.Length < headerSize)
                            {
                                // examined 1 byte, did not consume anything
                                _networkConnectionReader.AdvanceTo(result.Buffer.Start, result.Buffer.GetPosition(1));
                                result = await _networkConnectionReader.ReadAtLeastAsync(
                                    headerSize,
                                    CancellationToken.None).ConfigureAwait(false);

                                if (result.Buffer.Length < headerSize)
                                {
                                    throw new ConnectionLostException();
                                }
                            }

                            EncapsulationHeader encapsulationHeader = Encoding.Slice11.DecodeBuffer(
                                result.Buffer.Slice(1, 6),
                                (ref SliceDecoder decoder) => new EncapsulationHeader(ref decoder));

                            _networkConnectionReader.AdvanceTo(result.Buffer.GetPosition(headerSize));

                            payloadSize = encapsulationHeader.EncapsulationSize - 6;

                            if (payloadSize != frameRemainderSize - headerSize)
                            {
                                throw new InvalidDataException(
                                    @$"response payload size/frame size mismatch: payload size is {payloadSize
                                    } bytes but frame has {frameRemainderSize - headerSize} bytes left");
                            }

                            // TODO: check encoding is 1.1. See github proposal.
                        }
                        else
                        {
                            // An ice system exception

                            // examined 1 byte, did not consume anything
                            _networkConnectionReader.AdvanceTo(result.Buffer.Start, result.Buffer.GetPosition(1));

                            payloadSize = frameRemainderSize;
                        }

                        PipeReader payloadReader = await CreateResponsePayloadPipeReaderAsync(
                            payloadSize,
                            _networkConnectionReader,
                            _memoryPool,
                            _minimumSegmentSize,
                            CancellationToken.None).ConfigureAwait(false);

                        ResultType resultType = replyStatus switch
                        {
                            ReplyStatus.OK => ResultType.Success,
                            ReplyStatus.UserException => (ResultType)SliceResultType.ServiceFailure,
                            _ => ResultType.Failure
                        };

                        lock (_mutex)
                        {
                            Debug.Assert(requestId != null);
                            if (_invocations.TryGetValue(requestId.Value, out OutgoingRequest? request))
                            {
                                // For compatibility with ZeroC Ice "indirect" proxies
                                if (replyStatus == ReplyStatus.ObjectNotExistException &&
                                    request.Proxy.Endpoint == null)
                                {
                                    request.Features = request.Features.With(RetryPolicy.OtherReplica);
                                }

                                request.Features.Get<IceOutgoingRequest>()!.IncomingResponseCompletionSource.SetResult(
                                    new IncomingResponse(request)
                                    {
                                        Payload = payloadReader,
                                        ResultType = resultType
                                    });
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
                        break;
                    }

                    default:
                    {
                        throw new InvalidDataException(
                            $"received Ice frame with unknown frame type '{prologue.FrameType}'");
                    }
                }
            } // while

            static (IcePrologue Prologue, int? RequestId, int Consumed) DecodePrologue(ReadOnlySequence<byte> buffer)
            {
                var decoder = new SliceDecoder(buffer, Encoding.Slice11);
                var prologue = new IcePrologue(ref decoder);
                int? requestId = null;
                if ((prologue.FrameType == IceFrameType.Reply || prologue.FrameType == IceFrameType.Request) &&
                    buffer.Length >= (IceDefinitions.PrologueSize + 4))
                {
                    requestId = decoder.DecodeInt();
                }
                return (prologue, requestId, (int)decoder.Consumed);
            }

            static (IceRequestHeader Header, int Consumed) DecodeRequestHeader(ReadOnlySequence<byte> buffer)
            {
                var decoder = new SliceDecoder(buffer, Encoding.Slice11);
                var requestHeader = new IceRequestHeader(ref decoder);

                int payloadSize = requestHeader.EncapsulationHeader.EncapsulationSize - 6;
                if (payloadSize != (buffer.Length - decoder.Consumed))
                {
                    throw new InvalidDataException(@$"request payload size mismatch: expected {payloadSize
                        } bytes, read {buffer.Length - decoder.Consumed} bytes");
                }

                return (requestHeader, (int)decoder.Consumed);
            }

            // Creates a pipe reader to simplify the reading of the payload from an incoming ice request.
            // The payload is buffered into an internal pipe. The size is written first as a varulong followed by the
            // payload.
            static PipeReader CreateRequestPayloadPipeReader(
                ReadOnlySequence<byte> payload,
                MemoryPool<byte> pool,
                int minimumSegmentSize)
            {
                var pipe = new Pipe(new PipeOptions(
                    pool: pool,
                    minimumSegmentSize: minimumSegmentSize,
                    pauseWriterThreshold: 0,
                    writerScheduler: PipeScheduler.Inline));

                var encoder = new SliceEncoder(pipe.Writer, Encoding.Slice20);
                encoder.EncodeSize(checked((int)payload.Length));

                // Copy the payload data to the internal pipe writer.
                // TODO: this full payload copying is temporary.
                if (!payload.IsEmpty)
                {
                    pipe.Writer.Write(payload);
                }

                // No more data to consume for the payload so we complete the internal pipe writer.
                pipe.Writer.Complete();
                return pipe.Reader;
            }

            // Creates a pipe reader to simplify the reading of the payload from an incoming ice response.
            // The payload is buffered into an internal pipe. The size is written first as a varulong followed by the
            // payload.
            static async ValueTask<PipeReader> CreateResponsePayloadPipeReaderAsync(
                int payloadSize,
                SimpleNetworkConnectionPipeReader networkConnectionReader,
                MemoryPool<byte> pool,
                int minimumSegmentSize,
                CancellationToken cancel)
            {
                var pipe = new Pipe(new PipeOptions(
                    pool: pool,
                    minimumSegmentSize: minimumSegmentSize,
                    pauseWriterThreshold: 0,
                    writerScheduler: PipeScheduler.Inline));

                EncodeSize(payloadSize, pipe.Writer);

                await networkConnectionReader.FillBufferWriterAsync(
                    pipe.Writer,
                    payloadSize,
                    cancel).ConfigureAwait(false);

                await pipe.Writer.CompleteAsync().ConfigureAwait(false);

                return pipe.Reader;

                static void EncodeSize(int payloadSize, PipeWriter into)
                {
                    // We always encode the size as a varulong.
                    var encoder = new SliceEncoder(into, Encoding.Slice20);
                    encoder.EncodeSize(payloadSize);
                }
            }
        }
    }
}
