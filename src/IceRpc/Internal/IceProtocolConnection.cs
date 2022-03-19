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
        private readonly SimpleNetworkConnectionPipeWriter _networkConnectionWriter;
        private readonly SimpleNetworkConnectionPipeReader _networkConnectionReader;
        private int _nextRequestId;
        private readonly Configure.IceProtocolOptions _options;
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
                await request.CompleteAsync(new ConnectionClosedException()).ConfigureAwait(false);
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

            int requestId = requestFeature.Id;

            // Wait for the response.
            try
            {
                PipeReader frameReader =  await requestFeature.IncomingResponseCompletionSource.Task.WaitAsync(
                    cancel).ConfigureAwait(false);

                try
                {
                    if (!frameReader.TryRead(out ReadResult readResult))
                    {
                        throw new InvalidDataException($"received empty response frame for request #{requestId}");
                    }

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

                        // Consume header.
                        frameReader.AdvanceTo(readResult.Buffer.GetPosition(headerSize));

                        // Sanity check
                        int payloadSize = encapsulationHeader.EncapsulationSize - 6;
                        if (payloadSize != readResult.Buffer.Length - headerSize)
                        {
                            throw new InvalidDataException(
                                @$"response payload size/frame size mismatch: payload size is {payloadSize
                                } bytes but frame has {readResult.Buffer.Length - headerSize} bytes left");
                        }

                        // TODO: check encoding is 1.1. See github proposal.
                    }
                    else
                    {
                        // An ice system exception

                        // examined 1 byte, did not consume anything
                        frameReader.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.GetPosition(1));
                    }

                    // For compatibility with ZeroC Ice "indirect" proxies
                    if (replyStatus == ReplyStatus.ObjectNotExistException && request.Proxy.Endpoint == null)
                    {
                        request.Features = request.Features.With(RetryPolicy.OtherReplica);
                    }

                    return new IncomingResponse(request)
                    {
                        Payload = frameReader,
                        ResultType = replyStatus switch
                        {
                            ReplyStatus.OK => ResultType.Success,
                            ReplyStatus.UserException => (ResultType)SliceResultType.ServiceFailure,
                            _ => ResultType.Failure
                        }
                    };
                }
                catch (Exception ex)
                {
                    await frameReader.CompleteAsync(ex).ConfigureAwait(false);
                    throw;
                }
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
                throw new InvalidOperationException("cannot send twoway request over UDP");
            }
            if (request.PayloadSourceStream != null)
            {
                throw new NotSupportedException("PayloadSourceStream must be null with the ice protocol");
            }

            try
            {
                // Read payload source until IsCompleted is true.

                ReadResult readResult = await request.PayloadSource.ReadAtLeastAsync(
                    _options.MaxOutgoingFrameSize + 1, cancel).ConfigureAwait(false);

                if (!readResult.IsCompleted)
                {
                    throw new ArgumentException(
                        "payload size is greater than the max outgoing frame size",
                        nameof(request));
                }

                if (readResult.IsCanceled)
                {
                    throw new OperationCanceledException();
                }

                ReadOnlySequence<byte> payload = readResult.Buffer;
                int payloadSize = checked((int)payload.Length);

                // Wait for sending of other frames to complete. The semaphore is used as an asynchronous queue to
                // serialize the sending of frames.
                await _sendSemaphore.EnterAsync(cancel).ConfigureAwait(false);

                try
                {
                    // Assign the request ID for twoway invocations and keep track of the invocation for receiving the
                    // response.
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
                            _invocations[requestId] = request;
                            request.Features = request.Features.With(new IceOutgoingRequest(requestId));
                        }
                    }

                    // If the application sets the payload sink, the initial payload sink is set and we need to set the
                    // stream output on the delayed pipe writer decorator. Otherwise, we directly use the stream output.
                    PipeWriter payloadSink;
                    if (request.InitialPayloadSink == null)
                    {
                        payloadSink = _payloadWriter;
                    }
                    else
                    {
                        request.InitialPayloadSink.SetDecoratee(_payloadWriter);
                        payloadSink = request.PayloadSink;
                    }

                    int frameSize = EncodeHeader(_networkConnectionWriter, request, requestId, payloadSize);
                    if (frameSize > _options.MaxOutgoingFrameSize)
                    {
                        throw new ArgumentException(
                            "request frame size is greater than the max outgoing frame size",
                            nameof(request));
                    }

                    await SendPayloadAsync(payload, request.PayloadSource, payloadSink, cancel).ConfigureAwait(false);
                    request.IsSent = true;
                }
                finally
                {
                    // None of the CompleteAsync later on can write to the network.
                    _sendSemaphore.Release();
                }
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

            static int EncodeHeader(PipeWriter output, OutgoingRequest request, int requestId, int payloadSize)
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

                int frameSize = encoder.EncodedByteCount + payloadSize;
                SliceEncoder.EncodeInt(frameSize, sizePlaceholder.Span);
                return frameSize;
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
                await response.CompleteAsync().ConfigureAwait(false);
                return;
            }
            if (response.PayloadSourceStream != null)
            {
                throw new NotSupportedException("PayloadSourceStream must be null with the ice protocol");
            }

            Debug.Assert(!_isUdp); // udp is oneway-only so no response

            if (request.Features.Get<IceIncomingRequest>() is not IceIncomingRequest requestFeature)
            {
                throw new InvalidOperationException("request ID feature is not set");
            }

            try
            {
                // Read payload source until IsCompleted is true.

                ReadResult readResult = await response.PayloadSource.ReadAtLeastAsync(
                    _options.MaxOutgoingFrameSize + 1,
                    cancel).ConfigureAwait(false);

                if (!readResult.IsCompleted)
                {
                    throw new ArgumentException(
                        "payload size is greater than the max outgoing frame size",
                        nameof(response));
                }

                if (readResult.IsCanceled)
                {
                    throw new OperationCanceledException();
                }

                ReadOnlySequence<byte> payload = readResult.Buffer;
                int payloadSize = checked((int)payload.Length);

                // Wait for sending of other frames to complete. The semaphore is used as an asynchronous queue to
                // serialize the sending of frames.
                await _sendSemaphore.EnterAsync(cancel).ConfigureAwait(false);

                try
                {
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

                    int frameSize = EncodeHeader(_networkConnectionWriter, requestFeature.Id, payloadSize, replyStatus);
                    if (frameSize > _options.MaxOutgoingFrameSize)
                    {
                        throw new ArgumentException(
                            "response frame size is greater than the max outgoing frame size",
                            nameof(response));
                    }

                    await SendPayloadAsync(
                        payload,
                        response.PayloadSource,
                        response.PayloadSink,
                        cancel).ConfigureAwait(false);
                }
                finally
                {
                    _sendSemaphore.Release();
                }
            }
            catch (Exception ex)
            {
                await response.CompleteAsync(ex).ConfigureAwait(false);
                throw;
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

            static int EncodeHeader(PipeWriter writer, int requestId, int payloadSize, ReplyStatus replyStatus)
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
                return frameSize;
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

        /// <summary>Sends the payload source of an outgoing frame alongside any header previously buffered.</summary>
        private static async ValueTask SendPayloadAsync(
            ReadOnlySequence<byte> payload,
            PipeReader payloadSource,
            PipeWriter payloadSink,
            CancellationToken cancel)
        {
            FlushResult flushResult;

            try
            {
                if (payload.Length > 0)
                {
                    if (payload.IsSingleSegment)
                    {
                        flushResult = await payloadSink.WriteAsync(payload.First, cancel).ConfigureAwait(false);
                        CheckFlushResult(flushResult);
                    }
                    else if (payloadSink is IcePayloadPipeWriter icePayloadPipeWriter)
                    {
                        await icePayloadPipeWriter.WriteAsync(payload, cancel).ConfigureAwait(false);
                    }
                    else
                    {
                        // TODO: If readResult.Buffer.Length is small, it might be better to Write these buffers (i.e.
                        // copy them to the unflushed bytes) instead of calling multiple times WriteAsync that can end
                        // up in multiple network calls.
                        foreach (ReadOnlyMemory<byte> memory in payload)
                        {
                            flushResult = await payloadSink.WriteAsync(memory, cancel).ConfigureAwait(false);
                            CheckFlushResult(flushResult);
                        }
                    }
                }
            }
            finally
            {
                // The "writer" on the other side of this payloadSource is completed, so there is no point to pass an
                // exception to CompleteAsync.
                await payloadSource.CompleteAsync().ConfigureAwait(false);
            }

            // FlushAsync on the underlying SimpleNetworkConnectionPipeWriter is no-op when there is no unflushed byte
            // (typically because one of the WriteAsync above flushed all the bytes).
            // We need to call FlushAsync no matter what in case an interceptor or middleware decides to implement
            // WriteAsync by sending everything to the unflushed bytes, which is a legitimate implementation.
            flushResult = await payloadSink.FlushAsync(cancel).ConfigureAwait(false);
            CheckFlushResult(flushResult);

            await payloadSink.CompleteAsync().ConfigureAwait(false);

            static void CheckFlushResult(FlushResult flushResult)
            {
                if (flushResult.IsCanceled || flushResult.IsCompleted)
                {
                    throw new NotSupportedException("unexpected flush result");
                }
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
                if (prologue.FrameSize > _options.MaxIncomingFrameSize)
                {
                    throw new InvalidDataException(
                        $"incoming frame size ({prologue.FrameSize}) is greater than max incoming frame size");
                }
                else if (_isUdp &&
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

                        PipeReader frameReader = await CreateFrameReaderAsync(
                            frameRemainderSize,
                            _networkConnectionReader,
                            _memoryPool,
                            _minimumSegmentSize,
                            CancellationToken.None).ConfigureAwait(false);

                        bool cleanupFrameReader = true;

                        try
                        {
                            lock (_mutex)
                            {
                                if (_invocations.TryGetValue(requestId.Value, out OutgoingRequest? request))
                                {
                                    request.Features.Get<IceOutgoingRequest>()!.IncomingResponseCompletionSource.SetResult(
                                        frameReader);

                                    cleanupFrameReader = false;
                                }
                                else if (!_shutdown)
                                {
                                    throw new InvalidDataException("received ice Reply for unknown invocation");
                                }
                            }
                        }
                        finally
                        {
                            if (cleanupFrameReader)
                            {
                                await frameReader.CompleteAsync().ConfigureAwait(false);
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

            // Creates a pipe reader to simplify the reading of a request or response frame. The frame is read fully and
            // buffered into an internal pipe.
            static async ValueTask<PipeReader> CreateFrameReaderAsync(
                int size,
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

                await networkConnectionReader.FillBufferWriterAsync(
                    pipe.Writer,
                    size,
                    cancel).ConfigureAwait(false);

                await pipe.Writer.CompleteAsync().ConfigureAwait(false);

                return pipe.Reader;
            }
        }
    }
}
