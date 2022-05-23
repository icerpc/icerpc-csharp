// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
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

        public TimeSpan LastActivity => _networkConnection.LastActivity;

        public Action<string>? PeerShutdownInitiated { get; set; }

        public Protocol Protocol => Protocol.Ice;

        private static readonly IDictionary<RequestFieldKey, ReadOnlySequence<byte>> _idempotentFields =
            new Dictionary<RequestFieldKey, ReadOnlySequence<byte>>
            {
                [RequestFieldKey.Idempotent] = default
            }.ToImmutableDictionary();

        private static readonly IDictionary<ResponseFieldKey, ReadOnlySequence<byte>> _otherReplicaFields =
            new Dictionary<ResponseFieldKey, ReadOnlySequence<byte>>
            {
                [ResponseFieldKey.RetryPolicy] = new ReadOnlySequence<byte>(new byte[]
                {
                    (byte)Retryable.OtherReplica
                })
            }.ToImmutableDictionary();

        private readonly IDispatcher _dispatcher;
        private readonly TaskCompletionSource _dispatchesAndInvocationsCompleted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly HashSet<CancellationTokenSource> _dispatches = new();
        private readonly AsyncSemaphore? _dispatchSemaphore;
        private readonly Dictionary<int, TaskCompletionSource<PipeReader>> _invocations = new();
        private bool _isAborted;
        private bool _isShutdown;
        private bool _isShuttingDown;
        private readonly MemoryPool<byte> _memoryPool;
        private readonly int _minimumSegmentSize;

        private readonly object _mutex = new();
        private readonly ISimpleNetworkConnection _networkConnection;
        private readonly SimpleNetworkConnectionReader _networkConnectionReader;
        private readonly SimpleNetworkConnectionWriter _networkConnectionWriter;
        private int _nextRequestId;
        private readonly Configure.ConnectionOptions _options;
        private readonly IcePayloadPipeWriter _payloadWriter;
        private readonly TaskCompletionSource _pendingClose = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenSource _readCancelSource = new();
        private TaskCompletionSource? _readFramesTaskCompletionSource;
        private readonly AsyncSemaphore _writeSemaphore = new(1, 1);

        public void Abort(Exception exception)
        {
            lock (_mutex)
            {
                if (_isAborted)
                {
                    return;
                }
                _isAborted = true;
            }

            // Close the network connection and cancel the pending receive.
            _networkConnection.Dispose();
            _readCancelSource.Cancel();

            CancelInvocations(exception);
            CancelDispatches();

            // Unblock dispatches waiting to execute.
            _dispatchSemaphore?.Complete(exception);

            // Unblock ShutdownAsync which might be waiting for the connection to be disposed.
            _pendingClose.TrySetResult();

            // Unblock ShutdownAsync if it's waiting for invocations and dispatches to complete.
            _dispatchesAndInvocationsCompleted.TrySetException(exception);

            _readCancelSource.Dispose();

            // Release remaining resources in the background.
            _ = AbortCoreAsync();

            async Task AbortCoreAsync()
            {
                // Unblock requests waiting on the semaphore and wait for the semaphore to be released to ensure we
                // don't dispose the simple network connection writer while it's being used.
                await _writeSemaphore.CompleteAndWaitAsync(exception).ConfigureAwait(false);

                // Wait for the receive task to complete to ensure we don't dispose the simple network connection reader
                // while it's being used.
                if (_readFramesTaskCompletionSource != null)
                {
                    await _readFramesTaskCompletionSource.Task.ConfigureAwait(false);
                }

                // It's now safe to dispose of the reader/writer since no more threads are sending/receiving data.
                _networkConnectionReader.Dispose();
                _networkConnectionWriter.Dispose();
            }
        }

        public async Task AcceptRequestsAsync(IConnection connection)
        {
            try
            {
                _readFramesTaskCompletionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

                // Read frames until the CloseConnection frame is received.
                await ReadFramesAsync(connection, _readCancelSource.Token).ConfigureAwait(false);
            }
            catch (ConnectionLostException) when (_isShutdown)
            {
                // Ignore, the peer closed the connection after we sent the CloseConnection frame.
            }
            finally
            {
                _readFramesTaskCompletionSource?.SetResult();
            }
        }

        public void Dispose() => Abort(new ConnectionClosedException());

        public async Task<IncomingResponse> InvokeAsync(
            OutgoingRequest request,
            IConnection connection,
            CancellationToken cancel)
        {
            bool acquiredSemaphore = false;
            int requestId = 0;
            TaskCompletionSource<PipeReader>? responseCompletionSource = null;
            PipeWriter payloadWriter = _payloadWriter;

            try
            {
                if (request.PayloadStream != null)
                {
                    throw new NotSupportedException("PayloadStream must be null with the ice protocol");
                }

                // Read the full payload. This can take some time so this needs to be done before acquiring the write
                // semaphore.
                ReadOnlySequence<byte> payload = await ReadFullPayloadAsync(
                    request.Payload,
                    cancel).ConfigureAwait(false);
                int payloadSize = checked((int)payload.Length);

                // Wait for writing of other frames to complete. The semaphore is used as an asynchronous queue to
                // serialize the writing of frames.
                await _writeSemaphore.EnterAsync(cancel).ConfigureAwait(false);
                acquiredSemaphore = true;

                // Assign the request ID for twoway invocations and keep track of the invocation for receiving the
                // response. The request ID is only assigned once the write semaphore is acquired. We don't want a
                // canceled request to allocate a request ID that won't be used.
                if (!request.IsOneway)
                {
                    lock (_mutex)
                    {
                        if (_isShuttingDown)
                        {
                            throw new ConnectionClosedException();
                        }
                        requestId = ++_nextRequestId;
                        responseCompletionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
                        _invocations[requestId] = responseCompletionSource;
                    }
                }

                EncodeRequestHeader(_networkConnectionWriter, request, requestId, payloadSize);

                payloadWriter = request.GetPayloadWriter(payloadWriter);

                FlushResult flushResult = await payloadWriter.WriteAsync(
                    payload,
                    endStream: false,
                    cancel).ConfigureAwait(false);

                // If a payload writer decorator returns a canceled or completed flush result, we have to throw
                // NotSupportedException. We can't interrupt the writing of a payload since it would lead to a bogus
                // payload to be sent over the connection.
                if (flushResult.IsCanceled || flushResult.IsCompleted)
                {
                    throw new NotSupportedException(
                        "payload writer cancellation or completion is not supported with the ice protocol");
                }

                await request.Payload.CompleteAsync().ConfigureAwait(false);
                await payloadWriter.CompleteAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                await payloadWriter.CompleteAsync(exception).ConfigureAwait(false);
                throw;
            }
            finally
            {
                if (acquiredSemaphore)
                {
                    _writeSemaphore.Release();
                }
            }

            // Request is sent at this point.
            request.IsSent = true;

            if (request.IsOneway)
            {
                // We're done, there's no response for oneway requests.
                return new IncomingResponse(request, connection);
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

                    ReplyStatus replyStatus = ((int)readResult.Buffer.FirstSpan[0]).AsReplyStatus();

                    if (replyStatus <= ReplyStatus.UserException)
                    {
                        const int headerSize = 7; // reply status byte + encapsulation header

                        // read and check encapsulation header (6 bytes long)

                        if (readResult.Buffer.Length < headerSize)
                        {
                            throw new ConnectionLostException();
                        }

                        EncapsulationHeader encapsulationHeader = SliceEncoding.Slice1.DecodeBuffer(
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

                        // TODO: check encoding is Slice1. See github proposal.

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
                    IDictionary<ResponseFieldKey, ReadOnlySequence<byte>> fields =
                        replyStatus == ReplyStatus.ObjectNotExistException && request.Proxy.Endpoint == null ?
                        _otherReplicaFields :
                        ImmutableDictionary<ResponseFieldKey, ReadOnlySequence<byte>>.Empty;

                    return new IncomingResponse(request, connection, fields)
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
                        if (_isShuttingDown && _invocations.Count == 0 && _dispatches.Count == 0)
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
                var encoder = new SliceEncoder(output, SliceEncoding.Slice1);

                // Write the request header.
                encoder.WriteByteSpan(IceDefinitions.FramePrologue);
                encoder.EncodeIceFrameType(IceFrameType.Request);
                encoder.EncodeUInt8(0); // compression status

                Span<byte> sizePlaceholder = encoder.GetPlaceholderSpan(4);

                encoder.EncodeInt32(requestId);

                byte encodingMajor = 1;
                byte encodingMinor = 1;

                var requestHeader = new IceRequestHeader(
                    request.Proxy.Path,
                    request.Proxy.Fragment,
                    request.Operation,
                    request.Fields.ContainsKey(RequestFieldKey.Idempotent) ?
                        OperationMode.Idempotent : OperationMode.Normal,
                    request.Features.Get<IContextFeature>()?.Value ?? ImmutableDictionary<string, string>.Empty,
                    new EncapsulationHeader(encapsulationSize: payloadSize + 6, encodingMajor, encodingMinor));
                requestHeader.Encode(ref encoder);

                int frameSize = checked(encoder.EncodedByteCount + payloadSize);
                SliceEncoder.EncodeInt32(frameSize, sizePlaceholder);
            }
        }

        public bool HasCompatibleParams(Endpoint remoteEndpoint) =>
            _networkConnection.HasCompatibleParams(remoteEndpoint);

        public async Task PingAsync(CancellationToken cancel)
        {
            await _writeSemaphore.EnterAsync(cancel).ConfigureAwait(false);
            try
            {
                EncodeValidateConnectionFrame(_networkConnectionWriter);
                // The flush can't be canceled because it would lead to the writing of an incomplete frame.
                await _networkConnectionWriter.FlushAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                _writeSemaphore.Release();
            }

            static void EncodeValidateConnectionFrame(SimpleNetworkConnectionWriter writer)
            {
                var encoder = new SliceEncoder(writer, SliceEncoding.Slice1);
                IceDefinitions.ValidateConnectionFrame.Encode(ref encoder);
            }
        }

        public async Task ShutdownAsync(string message, CancellationToken cancel)
        {
            bool alreadyShuttingDown = false;
            lock (_mutex)
            {
                if (_isShuttingDown)
                {
                    alreadyShuttingDown = true;
                }
                else
                {
                    _isShuttingDown = true;
                    if (_dispatches.Count == 0 && _invocations.Count == 0)
                    {
                        _dispatchesAndInvocationsCompleted.TrySetResult();
                    }
                }
            }

            cancel.Register(CancelDispatches);

            if (!alreadyShuttingDown)
            {
                // Cancel pending invocations immediately.
                CancelInvocations(new OperationCanceledException(message));

                // Wait for dispatches and invocations to complete.
                await _dispatchesAndInvocationsCompleted.Task.ConfigureAwait(false);

                // Mark the connection as shut down at this point. This is necessary to ensure ReadFramesAsync returns
                // successfully on a graceful connection shutdown. This needs to be set before writing the close
                // connection frame since the peer will close the simple network connection as soon as it receives the
                // frame.
                _isShutdown = true;

                await _writeSemaphore.EnterAsync(CancellationToken.None).ConfigureAwait(false);
                try
                {
                    // Encode and write the CloseConnection frame once all the dispatches are done.
                    EncodeCloseConnectionFrame(_networkConnectionWriter);
                    await _networkConnectionWriter.FlushAsync(CancellationToken.None).ConfigureAwait(false);
                }
                finally
                {
                    _writeSemaphore.Release();
                }
            }

            // When the peer receives the CloseConnection frame, the peer closes the connection. We wait for the
            // connection closure here. We can't just return and close the underlying transport since this could abort
            // the receive of the dispatch responses and close connection frame by the peer.
            await _pendingClose.Task.ConfigureAwait(false);

            static void EncodeCloseConnectionFrame(SimpleNetworkConnectionWriter writer)
            {
                var encoder = new SliceEncoder(writer, SliceEncoding.Slice1);
                IceDefinitions.CloseConnectionFrame.Encode(ref encoder);
            }
        }

        internal IceProtocolConnection(
            ISimpleNetworkConnection simpleNetworkConnection,
            IDispatcher dispatcher,
            Configure.ConnectionOptions options)
        {
            _dispatcher = dispatcher;
            _options = options;

            if (_options.MaxIceConcurrentDispatches > 0)
            {
                _dispatchSemaphore = new AsyncSemaphore(
                    initialCount: _options.MaxIceConcurrentDispatches,
                    maxCount: _options.MaxIceConcurrentDispatches);
            }

            // TODO: get the pool and minimum segment size from an option class, but which one? The Slic connection gets
            // these from SlicOptions but another option could be to add Pool/MinimunSegmentSize on
            // ConnectionOptions/ServerOptions. These properties would be used by:
            // - the multiplexed transport implementations
            // - the Ice protocol connection
            _memoryPool = MemoryPool<byte>.Shared;
            _minimumSegmentSize = 4096;

            _networkConnection = simpleNetworkConnection;
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

        internal async Task ConnectAsync(bool isServer, CancellationToken cancel)
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
                    throw new InvalidDataException(
                        @$"expected '{nameof(IceFrameType.ValidateConnection)
                        }' frame but received frame type '{validateConnectionFrame.FrameType}'");
                }
            }

            static void EncodeValidateConnectionFrame(SimpleNetworkConnectionWriter writer)
            {
                var encoder = new SliceEncoder(writer, SliceEncoding.Slice1);
                IceDefinitions.ValidateConnectionFrame.Encode(ref encoder);
            }

            static (IcePrologue, long) DecodeValidateConnectionFrame(ReadOnlySequence<byte> buffer)
            {
                var decoder = new SliceDecoder(buffer, SliceEncoding.Slice1);
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

            // Unblock invocations which are waiting on the write semaphore.
            _writeSemaphore.CancelAwaiters(exception);

            // Unblock invocations which are waiting for the reply frame.
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

        /// <summary>Reads the full Ice payload from the given pipe reader.</summary>
        private static async ValueTask<ReadOnlySequence<byte>> ReadFullPayloadAsync(
            PipeReader payload,
            CancellationToken cancel)
        {
            // We use ReadAtLeastAsync instead of ReadAsync to bypass the PauseWriterThreshold when the payload is
            // backed by a Pipe.
            ReadResult readResult = await payload.ReadAtLeastAsync(int.MaxValue, cancel).ConfigureAwait(false);

            if (readResult.IsCanceled)
            {
                throw new OperationCanceledException();
            }

            return readResult.IsCompleted ? readResult.Buffer :
                throw new ArgumentException("the payload size is greater than int.MaxValue", nameof(payload));
        }

        /// <summary>Read incoming frames and returns on graceful connection shutdown.</summary>
        /// <param name="connection">The connection assigned to <see cref="IncomingFrame.Connection"/>.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        private async ValueTask ReadFramesAsync(IConnection connection, CancellationToken cancel)
        {
            while (true)
            {
                ReadOnlySequence<byte> buffer = await _networkConnectionReader.ReadAtLeastAsync(
                    IceDefinitions.PrologueSize,
                    cancel).ConfigureAwait(false);

                // First decode and check the prologue.

                ReadOnlySequence<byte> prologueBuffer = buffer.Slice(0, IceDefinitions.PrologueSize);

                IcePrologue prologue = SliceEncoding.Slice1.DecodeBuffer(
                    prologueBuffer,
                    (ref SliceDecoder decoder) => new IcePrologue(ref decoder));

                _networkConnectionReader.AdvanceTo(prologueBuffer.End);

                IceDefinitions.CheckPrologue(prologue);
                if (prologue.FrameSize > _options.MaxIceFrameSize)
                {
                    throw new InvalidDataException(
                        $"received frame with size ({prologue.FrameSize}) greater than max frame size");
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

                        lock (_mutex)
                        {
                            // If local shutdown is in progress, shutdown from peer prevails. The local shutdown
                            // will return once the connection disposes this protocol connection.
                            _isShuttingDown = true;
                        }

                        // Call the peer shutdown initiated callback.
                        PeerShutdownInitiated?.Invoke("connection shutdown by peer");
                        return;
                    }

                    case IceFrameType.Request:
                        await ReadRequestAsync(prologue.FrameSize).ConfigureAwait(false);
                        break;

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
                        await ReadReplyAsync(prologue.FrameSize).ConfigureAwait(false);
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

            async Task ReadReplyAsync(int replyFrameSize)
            {
                // Read the remainder of the frame immediately into frameReader.
                PipeReader replyFrameReader = await CreateFrameReaderAsync(
                    replyFrameSize - IceDefinitions.PrologueSize,
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
                    int requestId = SliceEncoding.Slice1.DecodeBuffer(
                        requestIdBuffer,
                        (ref SliceDecoder decoder) => decoder.DecodeInt32());
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
                        else if (!_isShuttingDown)
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
            }

            async Task ReadRequestAsync(int requestFrameSize)
            {
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

                var request = new IncomingRequest(connection)
                {
                    Fields = requestHeader.OperationMode == OperationMode.Normal ?
                        ImmutableDictionary<RequestFieldKey, ReadOnlySequence<byte>>.Empty : _idempotentFields,
                    Fragment = requestHeader.Fragment,
                    IsOneway = requestId == 0,
                    Operation = requestHeader.Operation,
                    Path = requestHeader.Path,
                    Payload = requestFrameReader,
                };

                if (requestHeader.Context.Count > 0)
                {
                    request.Features = request.Features.With<IContextFeature>(
                        new ContextFeature
                        {
                            Value = requestHeader.Context
                        });
                }

                CancellationTokenSource? cancelDispatchSource = null;
                bool isShuttingDown = false;
                lock (_mutex)
                {
                    if (_isShuttingDown)
                    {
                        isShuttingDown = true;
                    }
                    else
                    {
                        cancelDispatchSource = new();
                        _dispatches.Add(cancelDispatchSource);
                    }
                }

                if (isShuttingDown)
                {
                    // If shutting down, ignore the incoming request.
                    // TODO: replace with payload exception and error code
                    await request.Payload.CompleteAsync(new ConnectionClosedException()).ConfigureAwait(false);
                }
                else
                {
                    if (_dispatchSemaphore is AsyncSemaphore dispatchSemaphore)
                    {
                        // This prevents us from receiving any frame until WaitAsync returns.
                        try
                        {
                            await dispatchSemaphore.EnterAsync(cancel).ConfigureAwait(false);
                        }
                        catch (Exception exception)
                        {
                            await request.Payload.CompleteAsync(exception).ConfigureAwait(false);
                            throw;
                        }
                    }

                    Debug.Assert(cancelDispatchSource != null);
                    _ = Task.Run(() => DispatchRequestAsync(request, cancelDispatchSource), cancel);
                }

                async Task DispatchRequestAsync(IncomingRequest request, CancellationTokenSource cancelDispatchSource)
                {
                    using CancellationTokenSource _ = cancelDispatchSource;

                    OutgoingResponse response;
                    try
                    {
                        // The dispatcher can complete the incoming request payload to release its memory as soon as
                        // possible.
                        response = await _dispatcher.DispatchAsync(
                            request,
                            cancelDispatchSource.Token).ConfigureAwait(false);

                        if (response != request.Response)
                        {
                            throw new InvalidOperationException(
                                "the dispatcher did not return the last response created for this request");
                        }
                    }
                    catch (Exception exception)
                    {
                        // If we catch an exception, we return a failure response with a Slice-encoded payload.

                        if (exception is not DispatchException dispatchException ||
                            dispatchException.ConvertToUnhandled)
                        {
                            dispatchException = exception is OperationCanceledException ?
                                new DispatchException("dispatch canceled by peer", DispatchErrorCode.Canceled) :
                                new DispatchException(
                                    message: null,
                                    exception is InvalidDataException ?
                                        DispatchErrorCode.InvalidData : DispatchErrorCode.UnhandledException,
                                    exception);
                        }

                        response = new OutgoingResponse(request)
                        {
                            Payload = CreateExceptionPayload(dispatchException, request),
                            ResultType = ResultType.Failure
                        };

                        static PipeReader CreateExceptionPayload(
                            DispatchException dispatchException,
                            IncomingRequest request)
                        {
                            ISliceEncodeFeature encodeFeature = request.GetFeature<ISliceEncodeFeature>() ??
                                SliceEncodeFeature.Default;

                            var pipe = new Pipe(encodeFeature.PipeOptions);

                            var encoder = new SliceEncoder(pipe.Writer, SliceEncoding.Slice1);
                            encoder.EncodeSystemException(
                                dispatchException,
                                request.Path,
                                request.Fragment,
                                request.Operation);
                            pipe.Writer.Complete(); // flush to reader and sets Is[Writer]Completed to true.
                            return pipe.Reader;
                        }
                    }
                    finally
                    {
                        // Even when the code above throws an exception, we catch it and write a response. So we never
                        // want to give an exception to CompleteAsync when completing the incoming payload.
                        await request.Payload.CompleteAsync().ConfigureAwait(false);
                    }

                    // The writing of the response can't be canceled. This would lead to invalid protocol behavior.
                    CancellationToken cancel = CancellationToken.None;

                    PipeWriter payloadWriter = _payloadWriter;
                    bool acquiredSemaphore = false;

                    Exception? completeException = null;
                    try
                    {
                        if (response.PayloadStream != null)
                        {
                            throw new NotSupportedException("PayloadStream must be null with the ice protocol");
                        }

                        if (request.IsOneway)
                        {
                            return;
                        }

                        // Read the full payload. This can take some time so this needs to be done before acquiring the
                        // write semaphore.
                        ReadOnlySequence<byte> payload = await ReadFullPayloadAsync(
                            response.Payload,
                            cancel).ConfigureAwait(false);
                        int payloadSize = checked((int)payload.Length);

                        // Wait for writing of other frames to complete. The semaphore is used as an asynchronous queue
                        // to serialize the writing of frames.
                        await _writeSemaphore.EnterAsync(cancel).ConfigureAwait(false);
                        acquiredSemaphore = true;

                        ReplyStatus replyStatus = ReplyStatus.OK;

                        if (response.ResultType != ResultType.Success)
                        {
                            if (response.ResultType == ResultType.Failure)
                            {
                                replyStatus = ((int)payload.FirstSpan[0]).AsReplyStatus();

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

                        payloadWriter = response.GetPayloadWriter(payloadWriter);

                        // Write the payload and complete the source.
                        FlushResult flushResult = await payloadWriter.WriteAsync(
                            payload,
                            endStream: false,
                            cancel).ConfigureAwait(false);

                        // If a payload writer decorator returns a canceled or completed flush result, we have to throw
                        // NotSupportedException. We can't interrupt the writing of a payload since it would lead to a
                        // bogus payload to be sent over the connection.
                        if (flushResult.IsCanceled || flushResult.IsCompleted)
                        {
                            throw new NotSupportedException(
                                "payload writer cancellation or completion is not supported with the ice protocol");
                        }

                        await payloadWriter.CompleteAsync().ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        request.Complete(exception);
                        await payloadWriter.CompleteAsync(exception).ConfigureAwait(false);

                        // This is an unrecoverable failure, so we kill the connection.
                        _networkConnection.Dispose();
                    }
                    finally
                    {
                        request.Complete(completeException);

                        if (acquiredSemaphore)
                        {
                            _writeSemaphore.Release();
                        }

                        lock (_mutex)
                        {
                            _dispatchSemaphore?.Release();

                            // Dispatch is done, remove the cancellation token source for the dispatch.
                            if (_dispatches.Remove(cancelDispatchSource))
                            {
                                // If no more invocations or dispatches and shutting down, shutdown can complete.
                                if (_isShuttingDown && _invocations.Count == 0 && _dispatches.Count == 0)
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
                        var encoder = new SliceEncoder(writer, SliceEncoding.Slice1);

                        // Write the response header.

                        encoder.WriteByteSpan(IceDefinitions.FramePrologue);
                        encoder.EncodeIceFrameType(IceFrameType.Reply);
                        encoder.EncodeUInt8(0); // compression status
                        Span<byte> sizePlaceholder = encoder.GetPlaceholderSpan(4);

                        encoder.EncodeInt32(requestId);

                        if (replyStatus <= ReplyStatus.UserException)
                        {
                            encoder.EncodeReplyStatus(replyStatus);

                            // When IceRPC receives a response, it ignores the response encoding. So this "1.1" is only
                            // relevant to a ZeroC Ice client that decodes the response. The only Slice encoding such a
                            // client can possibly use to decode the response payload is 1.1 or 1.0, and we don't care
                            // about interop with 1.0.
                            var encapsulationHeader = new EncapsulationHeader(
                                encapsulationSize: payloadSize + 6,
                                payloadEncodingMajor: 1,
                                payloadEncodingMinor: 1);
                            encapsulationHeader.Encode(ref encoder);
                        }
                        // else the reply status (> UserException) is part of the payload

                        int frameSize = encoder.EncodedByteCount + payloadSize;
                        SliceEncoder.EncodeInt32(frameSize, sizePlaceholder);
                    }
                }

                static (int RequestId, IceRequestHeader Header, int Consumed) DecodeRequestIdAndHeader(
                    ReadOnlySequence<byte> buffer)
                {
                    var decoder = new SliceDecoder(buffer, SliceEncoding.Slice1);

                    int requestId = decoder.DecodeInt32();
                    var requestHeader = new IceRequestHeader(ref decoder);

                    if (requestHeader.EncapsulationHeader.PayloadEncodingMajor != 1 ||
                        requestHeader.EncapsulationHeader.PayloadEncodingMinor != 1)
                    {
                        throw new InvalidDataException(
                            @$"unsupported payload encoding '{requestHeader.EncapsulationHeader.PayloadEncodingMajor
                            }.{requestHeader.EncapsulationHeader.PayloadEncodingMinor}'");
                    }

                    int payloadSize = requestHeader.EncapsulationHeader.EncapsulationSize - 6;
                    if (payloadSize != (buffer.Length - decoder.Consumed))
                    {
                        throw new InvalidDataException(
                            @$"request payload size mismatch: expected {payloadSize
                            } bytes, read {buffer.Length - decoder.Consumed} bytes");
                    }

                    return (requestId, requestHeader, (int)decoder.Consumed);
                }
            }
        }
    }
}
