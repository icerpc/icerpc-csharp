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
        private readonly SimpleNetworkConnectionReader _networkConnectionReader;
        private readonly SimpleNetworkConnectionWriter _networkConnectionWriter;

        private int _nextRequestId;
        private readonly Configure.IceProtocolOptions _options;
        private readonly IcePayloadPipeWriter _payloadWriter;
        private readonly TaskCompletionSource _pendingClose = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly AsyncSemaphore _sendSemaphore = new(1, 1);
        private bool _shutdown;

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

        /// <inheritdoc/>
        public async Task<IncomingRequest> ReceiveRequestAsync()
        {
            while (true)
            {
                // Wait for a request frame to be received.

                int frameSize; // the total size of the frame

                try
                {
                    frameSize = await ReceiveFrameAsync().ConfigureAwait(false);
                }
                catch
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

                // TODO: at this point - before reading the actual request frame - we could easily wait until
                // _dispatches.Count < MaxConcurrentDispatches.

                PipeReader frameReader = await CreateFrameReaderAsync(
                    frameSize - IceDefinitions.PrologueSize,
                    _networkConnectionReader,
                    _memoryPool,
                    _minimumSegmentSize,
                    CancellationToken.None).ConfigureAwait(false);

                try
                {
                    if (!frameReader.TryRead(out ReadResult readResult))
                    {
                        throw new InvalidDataException("received invalid request frame");
                    }

                    Debug.Assert(readResult.IsCompleted);

                    (int requestId, IceRequestHeader requestHeader, int consumed) = DecodeRequestIdAndHeader(
                        readResult.Buffer);
                    frameReader.AdvanceTo(readResult.Buffer.GetPosition(consumed));

                    var request = new IncomingRequest(Protocol.Ice)
                    {
                        Fields = requestHeader.OperationMode == OperationMode.Normal ?
                                ImmutableDictionary<RequestFieldKey, ReadOnlySequence<byte>>.Empty : _idempotentFields,
                        Fragment = requestHeader.Fragment,
                        IsOneway = requestId == 0,
                        Operation = requestHeader.Operation,
                        Path = requestHeader.Path,
                        Payload = frameReader,
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

                    // If shutting down, ignore the incoming request and continue receiving frames until the connection
                    // is closed.
                    await request.Payload.CompleteAsync(new ConnectionClosedException()).ConfigureAwait(false);
                }
                catch
                {
                    await frameReader.CompleteAsync().ConfigureAwait(false);
                    throw;
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
                PipeReader frameReader = await requestFeature.IncomingResponseCompletionSource.Task.WaitAsync(
                    cancel).ConfigureAwait(false);

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
                        Payload = frameReader,
                        ResultType = replyStatus switch
                        {
                            ReplyStatus.OK => ResultType.Success,
                            ReplyStatus.UserException => (ResultType)SliceResultType.ServiceFailure,
                            _ => ResultType.Failure
                        }
                    };
                }
                catch
                {
                    await frameReader.CompleteAsync().ConfigureAwait(false);
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
            bool acquiredSemaphore = false;
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

                int frameSize = EncodeHeader(_networkConnectionWriter, request, requestId, payloadSize);

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
                    throw new NotSupportedException("payload sink cancellation or completion is not supported");
                }

                await request.PayloadSink.CompleteAsync().ConfigureAwait(false);
                await request.PayloadSource.CompleteAsync().ConfigureAwait(false);

                request.IsSent = true;
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

            static int EncodeHeader(
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
                    new EncapsulationHeader(encapsulationSize: checked(payloadSize + 6), encodingMajor, encodingMinor));
                requestHeader.Encode(ref encoder);

                int frameSize = checked(encoder.EncodedByteCount + payloadSize);
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

                if (request.Features.Get<IceIncomingRequest>() is not IceIncomingRequest requestFeature)
                {
                    throw new InvalidOperationException("request ID feature is not set");
                }

                // Read the full payload source. This can take some time so this needs to be done before acquiring the
                // send semaphore.
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

                int frameSize = EncodeHeader(_networkConnectionWriter, requestFeature!.Id, payloadSize, replyStatus);

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
                    throw new NotSupportedException("payload sink cancellation or completion is not supported");
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

            static int EncodeHeader(
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
                        encapsulationSize: checked(payloadSize + 6),
                        payloadEncodingMajor: 1,
                        payloadEncodingMinor: 1);
                    encapsulationHeader.Encode(ref encoder);
                }
                // else the reply status (> UserException) is part of the payload

                int frameSize = checked(encoder.EncodedByteCount + payloadSize);
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
                else if (_isUdp &&
                    (prologue.FrameSize > buffer.Length || prologue.FrameSize > UdpUtils.MaxPacketSize))
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
                        // the caller will read the request from _networkConnectionReader
                        return prologue.FrameSize;

                    case IceFrameType.RequestBatch:
                        // Read and ignore
                        PipeReader reader = await CreateFrameReaderAsync(
                            prologue.FrameSize - IceDefinitions.PrologueSize,
                            _networkConnectionReader,
                            _memoryPool,
                            _minimumSegmentSize,
                            CancellationToken.None).ConfigureAwait(false);
                        await reader.CompleteAsync().ConfigureAwait(false);
                        break;

                    case IceFrameType.Reply:
                        // Read the remainder of the frame immediately into frameReader.
                        PipeReader frameReader = await CreateFrameReaderAsync(
                            prologue.FrameSize - IceDefinitions.PrologueSize,
                            _networkConnectionReader,
                            _memoryPool,
                            _minimumSegmentSize,
                            CancellationToken.None).ConfigureAwait(false);

                        bool cleanupFrameReader = true;

                        try
                        {
                            // Read and decode request ID
                            if (!frameReader.TryRead(out ReadResult readResult) || readResult.Buffer.Length < 4)
                            {
                                throw new ConnectionLostException();
                            }

                            ReadOnlySequence<byte> requestIdBuffer = readResult.Buffer.Slice(0, 4);
                            int requestId = Encoding.Slice11.DecodeBuffer(
                                requestIdBuffer,
                                (ref SliceDecoder decoder) => decoder.DecodeInt());
                            frameReader.AdvanceTo(requestIdBuffer.End);

                            lock (_mutex)
                            {
                                if (_invocations.TryGetValue(requestId, out OutgoingRequest? request))
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
