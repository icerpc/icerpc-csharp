// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Slice.Internal;
using IceRpc.Transports;
using IceRpc.Transports.Internal;
using System.Buffers;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Internal;

internal sealed class IceProtocolConnection : ProtocolConnection
{
    public override ServerAddress ServerAddress => _duplexConnection.ServerAddress;

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

    private IConnectionContext? _connectionContext; // non-null once the connection is established
    private readonly IDispatcher _dispatcher;

    private int _dispatchCount;
    private readonly TaskCompletionSource _dispatchesAndInvocationsCompleted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly CancellationTokenSource _dispatchesAndInvocationsCts = new();
    private readonly SemaphoreSlim? _dispatchSemaphore;
    private readonly IDuplexConnection _duplexConnection;
    private readonly DuplexConnectionReader _duplexConnectionReader;
    private readonly DuplexConnectionWriter _duplexConnectionWriter;
    private readonly Dictionary<int, TaskCompletionSource<PipeReader>> _invocations = new();
    // Whether or not the inner exception details should be included in dispatch exceptions
    private readonly bool _includeInnerExceptionDetails;
    private bool _isReadOnly;
    private readonly int _maxFrameSize;
    private readonly MemoryPool<byte> _memoryPool;
    private readonly int _minSegmentSize;
    private readonly object _mutex = new();
    private int _nextRequestId;
    private readonly IcePayloadPipeWriter _payloadWriter;
    private readonly TaskCompletionSource _pendingClose = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task _pingTask = Task.CompletedTask;
    private Task? _readFramesTask;
    private readonly CancellationTokenSource _tasksCts = new();
    private readonly AsyncSemaphore _writeSemaphore = new(1, 1);

    internal IceProtocolConnection(
        IDuplexConnection duplexConnection,
        bool isServer,
        ConnectionOptions options)
        : base(isServer, options)
    {
        // With ice, we always listen for incoming frames (responses) so we need a dispatcher for incoming requests even
        // if we don't expect any. This dispatcher throws an ice ObjectNotExistException back to the client, which makes
        // more sense than throwing an UnknownException.
        _dispatcher = options.Dispatcher ?? ServiceNotFoundDispatcher.Instance;
        _maxFrameSize = options.MaxIceFrameSize;

        if (options.MaxDispatches > 0)
        {
            _dispatchSemaphore = new SemaphoreSlim(
                initialCount: options.MaxDispatches,
                maxCount: options.MaxDispatches);
        }
        _includeInnerExceptionDetails = options.IncludeInnerExceptionDetails;

        _memoryPool = options.Pool;
        _minSegmentSize = options.MinSegmentSize;

        _duplexConnection = duplexConnection;
        _duplexConnectionWriter = new DuplexConnectionWriter(
            duplexConnection,
            _memoryPool,
            _minSegmentSize);
        _duplexConnectionReader = new DuplexConnectionReader(
            duplexConnection,
            idleTimeout: options.IdleTimeout,
            _memoryPool,
            _minSegmentSize,
            connectionLostAction: ConnectionLost,
            keepAliveAction: () =>
            {
                try
                {
                    lock (_mutex)
                    {
                        if (_pingTask.IsCompleted && !_tasksCts.IsCancellationRequested)
                        {
                            _pingTask = PingAsync(_tasksCts.Token);
                        }
                    }
                }
                catch
                {
                    // Ignore, the read frames task will fail if the connection fails.
                }
            });

        _payloadWriter = new IcePayloadPipeWriter(_duplexConnectionWriter);

        async Task PingAsync(CancellationToken cancellationToken)
        {
            // Make sure we execute the function without holding the connection mutex lock.
            await Task.Yield();

            try
            {
                await _writeSemaphore.EnterAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    EncodeValidateConnectionFrame(_duplexConnectionWriter);
                    await _duplexConnectionWriter.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    ConnectionLost(exception);
                }
                finally
                {
                    _writeSemaphore.Release();
                }
            }
            catch
            {
                // Connection disposed.
            }

            static void EncodeValidateConnectionFrame(DuplexConnectionWriter writer)
            {
                var encoder = new SliceEncoder(writer, SliceEncoding.Slice1);
                IceDefinitions.ValidateConnectionFrame.Encode(ref encoder);
            }
        }
    }

    private protected override void CancelDispatchesAndInvocations()
    {
        if (!_dispatchesAndInvocationsCts.IsCancellationRequested)
        {
            // Cancel dispatches and invocations for a speedy shutdown.
            _dispatchesAndInvocationsCts.Cancel();

            lock (_mutex)
            {
                _isReadOnly = true; // prevent new dispatches or invocations from being accepted.

                if (_invocations.Count == 0 && _dispatchCount == 0)
                {
                    _dispatchesAndInvocationsCompleted.TrySetResult();
                }
            }
        }
    }

    private protected override bool CheckIfIdle()
    {
        lock (_mutex)
        {
            // If idle, mark the connection as readonly to stop accepting new dispatches or invocations.
            if (_invocations.Count == 0 && _dispatchCount == 0)
            {
                _isReadOnly = true;
                ConnectionClosedException = new(ConnectionErrorCode.ClosedByIdle);
                return true;
            }
            else
            {
                return false;
            }
        }
    }

    private protected override async Task<TransportConnectionInformation> ConnectAsyncCore(
        CancellationToken cancellationToken)
    {
        TransportConnectionInformation transportConnectionInformation = await _duplexConnection.ConnectAsync(
            cancellationToken).ConfigureAwait(false);

        // This needs to be set before starting the read frames task below.
        _connectionContext = new ConnectionContext(this, transportConnectionInformation);

        // Wait for the transport connection establishment to enable the idle timeout check.
        _duplexConnectionReader.EnableIdleCheck();

        if (IsServer)
        {
            EncodeValidateConnectionFrame(_duplexConnectionWriter);
            await _duplexConnectionWriter.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            ReadOnlySequence<byte> buffer = await _duplexConnectionReader.ReadAtLeastAsync(
                IceDefinitions.PrologueSize,
                cancellationToken).ConfigureAwait(false);

            (IcePrologue validateConnectionFrame, long consumed) = DecodeValidateConnectionFrame(buffer);
            _duplexConnectionReader.AdvanceTo(buffer.GetPosition(consumed), buffer.End);

            IceDefinitions.CheckPrologue(validateConnectionFrame);
            if (validateConnectionFrame.FrameSize != IceDefinitions.PrologueSize)
            {
                throw new InvalidDataException(
                    $"received Ice frame with only '{validateConnectionFrame.FrameSize}' bytes");
            }
            if (validateConnectionFrame.FrameType != IceFrameType.ValidateConnection)
            {
                throw new InvalidDataException(
                    $"expected '{nameof(IceFrameType.ValidateConnection)}' frame but received frame type '{validateConnectionFrame.FrameType}'");
            }
        }

        _readFramesTask = Task.Run(
            async () =>
            {
                try
                {
                    // Read frames until the CloseConnection frame is received.
                    await ReadFramesAsync(_tasksCts.Token).ConfigureAwait(false);

                    ConnectionClosedException = new(ConnectionErrorCode.ClosedByPeer);

                    _tasksCts.Cancel();
                    await Task.WhenAll(
                        _pingTask,
                        _writeSemaphore.CompleteAndWaitAsync(ConnectionClosedException)).ConfigureAwait(false);

                    // The peer expects the connection to be closed as soon as the CloseConnection message is received.
                    // So there's no need to initiate shutdown, we just close the transport connection and notify the
                    // callback that the connection has been shutdown by the peer.
                    _duplexConnection.Dispose();

                    // Initiate the shutdown.
                    InitiateShutdown(ConnectionErrorCode.ClosedByPeer);
                }
                catch (TransportException exception) when (
                    exception.ErrorCode == TransportErrorCode.ConnectionReset &&
                    _isReadOnly &&
                    _dispatchesAndInvocationsCompleted.Task.IsCompleted)
                {
                    // Expected if the connection is shutting down and waiting for the peer to close the connection.
                    Debug.Assert(ConnectionClosedException is not null);
                }
                catch (OperationCanceledException)
                {
                    // This can occur if DisposeAsync is called. This can only be called on a connected connection so
                    // ConnectionClosedException should always be set at this point.
                    Debug.Assert(ConnectionClosedException is not null);
                }
                catch (Exception exception)
                {
                    ConnectionClosedException = new(
                        ConnectionErrorCode.ClosedByAbort,
                        "the connection was lost",
                        exception);

                    // Notify the ConnectionLost callback.
                    ConnectionLost(exception);
                }
                finally
                {
                    // Make sure to unblock ShutdownAsync if it's waiting for the connection closure.
                    _pendingClose.TrySetResult();

                    // Don't wait for DisposeAsync to be called to cancel dispatches and invocations which might still
                    // be running.
                    CancelDispatchesAndInvocations();
                }
            },
            CancellationToken.None);

        return transportConnectionInformation;

        static void EncodeValidateConnectionFrame(DuplexConnectionWriter writer)
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

    private protected override async ValueTask DisposeAsyncCore()
    {
        // Dispose triggers the cancellation of pending operations (invocations, dispatches, ...)
        var exception = new ConnectionException(ConnectionErrorCode.OperationAborted);

        // Before disposing the transport connection, cancel pending tasks which are using it wait for the tasks to
        // complete.
        _tasksCts.Cancel();
        await Task.WhenAll(
            _readFramesTask ?? Task.CompletedTask,
            _pingTask,
            _writeSemaphore.CompleteAndWaitAsync(exception)).ConfigureAwait(false);

        // Dispose the transport connection. This will abort the transport connection if it wasn't shutdown first.
        _duplexConnection.Dispose();

        // Cancel dispatches and invocations.
        CancelDispatchesAndInvocations();

        // Next, wait for dispatches and invocations to complete.
        await _dispatchesAndInvocationsCompleted.Task.ConfigureAwait(false);

        // It's now safe to dispose of the reader/writer since no more threads are sending/receiving data.
        _duplexConnectionReader.Dispose();
        _duplexConnectionWriter.Dispose();

        _tasksCts.Dispose();
        _dispatchesAndInvocationsCts.Dispose();
        _dispatchSemaphore?.Dispose();
    }

    private protected override async Task<IncomingResponse> InvokeAsyncCore(
        OutgoingRequest request,
        CancellationToken cancellationToken)
    {
        CancellationTokenSource? cts = null;

        lock (_mutex)
        {
            // Nothing prevents InvokeAsync to be called on a connection which is being shutdown or disposed. We check
            // for this condition here and throw ConnectionClosedException if necessary.
            if (_isReadOnly)
            {
                Debug.Assert(ConnectionClosedException is not null);
                throw ConnectionClosedException;
            }

            // _dispatchesAndInvocationsCts token can throw ObjectDisposedException so only create the
            // linked source if the connection is not disposed.
            cts = CancellationTokenSource.CreateLinkedTokenSource(
                _dispatchesAndInvocationsCts.Token,
                cancellationToken);
        }

        PipeReader? frameReader = null;
        int requestId = 0;
        Exception? completeException = null;
        try
        {
            if (request.PayloadStream is not null)
            {
                throw new NotSupportedException("PayloadStream must be null with the ice protocol");
            }

            // Read the full payload. This can take some time so this needs to be done before acquiring the write
            // semaphore.
            ReadOnlySequence<byte> payload = await ReadFullPayloadAsync(
                request.Payload,
                cts.Token).ConfigureAwait(false);
            int payloadSize = checked((int)payload.Length);

            // Wait for writing of other frames to complete. The semaphore is used as an asynchronous queue to
            // serialize the writing of frames.
            await _writeSemaphore.EnterAsync(cts.Token).ConfigureAwait(false);
            PipeWriter payloadWriter = _payloadWriter;
            TaskCompletionSource<PipeReader>? responseCompletionSource = null;
            try
            {
                // Assign the request ID for twoway invocations and keep track of the invocation for receiving the
                // response. The request ID is only assigned once the write semaphore is acquired. We don't want a
                // canceled request to allocate a request ID that won't be used.
                if (!request.IsOneway)
                {
                    lock (_mutex)
                    {
                        if (_isReadOnly)
                        {
                            Debug.Assert(ConnectionClosedException is not null);
                            throw ConnectionClosedException;
                        }
                        else
                        {
                            if (_invocations.Count == 0 && _dispatchCount == 0)
                            {
                                DisableIdleCheck();
                            }

                            requestId = ++_nextRequestId;
                            responseCompletionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
                            _invocations[requestId] = responseCompletionSource;
                        }
                    }
                }

                EncodeRequestHeader(_duplexConnectionWriter, request, requestId, payloadSize);

                payloadWriter = request.GetPayloadWriter(payloadWriter);

                // The writing of the request can only be canceled if the connection is disposed.
                FlushResult flushResult = await payloadWriter.WriteAsync(
                    payload,
                    endStream: false,
                    _tasksCts.Token).ConfigureAwait(false);

                // If a payload writer decorator returns a canceled or completed flush result, we have to throw
                // NotSupportedException. We can't interrupt the writing of a payload since it would lead to a bogus
                // payload to be sent over the connection.
                if (flushResult.IsCanceled || flushResult.IsCompleted)
                {
                    throw new NotSupportedException(
                        "payload writer cancellation or completion is not supported with the ice protocol");
                }

                await request.Payload.CompleteAsync().ConfigureAwait(false);
            }
            finally
            {
                await payloadWriter.CompleteAsync(completeException).ConfigureAwait(false);

                _writeSemaphore.Release();
            }

            if (request.IsOneway)
            {
                // We're done, there's no response for oneway requests.
                return new IncomingResponse(request, _connectionContext!);
            }

            // Wait to receive the response.

            Debug.Assert(responseCompletionSource is not null);
            try
            {
                frameReader = await responseCompletionSource.Task.WaitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (responseCompletionSource.Task.IsCompleted)
            {
                // The WaitAsync might be canceled shortly after the response is received and before the task completion
                // source continuation is run.
                frameReader = await responseCompletionSource.Task.ConfigureAwait(false);
            }

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
                    throw new InvalidDataException($"received invalid frame header for request #{requestId}");
                }

                EncapsulationHeader encapsulationHeader = SliceEncoding.Slice1.DecodeBuffer(
                    readResult.Buffer.Slice(1, 6),
                    (ref SliceDecoder decoder) => new EncapsulationHeader(ref decoder));

                // Sanity check
                payloadSize = encapsulationHeader.EncapsulationSize - 6;
                if (payloadSize != readResult.Buffer.Length - headerSize)
                {
                    throw new InvalidDataException(
                        $"response payload size/frame size mismatch: payload size is {payloadSize} bytes but frame has {readResult.Buffer.Length - headerSize} bytes left");
                }

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
                replyStatus == ReplyStatus.ObjectNotExistException && request.ServiceAddress.ServerAddress is null ?
                _otherReplicaFields :
                ImmutableDictionary<ResponseFieldKey, ReadOnlySequence<byte>>.Empty;

            return new IncomingResponse(request, _connectionContext!, fields)
            {
                Payload = frameReader,
                ResultType = replyStatus switch
                {
                    ReplyStatus.Ok => ResultType.Success,
                    ReplyStatus.UserException => (ResultType)SliceResultType.ServiceFailure,
                    _ => ResultType.Failure
                }
            };
        }
        catch (OperationCanceledException exception)
        {
            if (_dispatchesAndInvocationsCts.IsCancellationRequested)
            {
                if (ConnectionClosedException is ConnectionException connectionException &&
                    connectionException.ErrorCode == ConnectionErrorCode.ClosedByAbort)
                {
                    // If the connection was lost, report a transport error.
                    throw new ConnectionException(
                        ConnectionErrorCode.TransportError,
                        connectionException.InnerException);
                }
                else
                {
                    // Otherwise, the invocation was canceled because of a speedy-shutdown.
                    throw new ConnectionException(ConnectionErrorCode.OperationAborted);
                }
            }
            else
            {
                completeException = exception;
                throw;
            }
        }
        catch (ConnectionException exception)
        {
            completeException = exception;
            throw;
        }
        catch (TransportException exception)
        {
            completeException = exception;
            throw new ConnectionException(ConnectionErrorCode.TransportError, exception);
        }
        catch (Exception exception)
        {
            completeException = exception;
            throw new ConnectionException(ConnectionErrorCode.Unspecified, exception);
        }
        finally
        {
            if (!request.IsOneway)
            {
                // Unregister the invocation.
                lock (_mutex)
                {
                    if (_invocations.Remove(requestId))
                    {
                        if (_invocations.Count == 0 && _dispatchCount == 0)
                        {
                            if (_isReadOnly)
                            {
                                _dispatchesAndInvocationsCompleted.TrySetResult();
                            }
                            else
                            {
                                EnableIdleCheck();
                            }
                        }
                    }
                }
            }

            cts?.Dispose();

            if (completeException is not null && frameReader is not null)
            {
                await frameReader.CompleteAsync(completeException).ConfigureAwait(false);
            }
        }

        static void EncodeRequestHeader(
            DuplexConnectionWriter output,
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

            // Request header.
            var requestHeader = new IceRequestHeader(
                request.ServiceAddress.Path,
                request.ServiceAddress.Fragment,
                request.Operation,
                request.Fields.ContainsKey(RequestFieldKey.Idempotent) ?
                    OperationMode.Idempotent : OperationMode.Normal);
            requestHeader.Encode(ref encoder);
            if (request.Fields.TryGetValue(RequestFieldKey.Context, out OutgoingFieldValue requestField))
            {
                requestField.Encode(ref encoder);
            }
            else
            {
                encoder.EncodeSize(0);
            }

            // We ignore all other fields. They can't be sent over ice.

            new EncapsulationHeader(
                encapsulationSize: payloadSize + 6,
                encodingMajor,
                encodingMinor).Encode(ref encoder);

            int frameSize = checked(encoder.EncodedByteCount + payloadSize);
            SliceEncoder.EncodeInt32(frameSize, sizePlaceholder);
        }
    }

    private protected override async Task ShutdownAsyncCore(CancellationToken cancellationToken)
    {
        lock (_mutex)
        {
            _isReadOnly = true;
            if (_dispatchCount == 0 && _invocations.Count == 0)
            {
                _dispatchesAndInvocationsCompleted.TrySetResult();
            }
        }

        if (_readFramesTask is not null)
        {
            // Wait for dispatches and invocations to complete.
            await _dispatchesAndInvocationsCompleted.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

            // Encode and write the CloseConnection frame once all the dispatches are done.
            try
            {
                await _writeSemaphore.EnterAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    EncodeCloseConnectionFrame(_duplexConnectionWriter);
                    await _duplexConnectionWriter.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    _writeSemaphore.Release();
                }
            }
            catch (ConnectionException exception) when (exception.ErrorCode == ConnectionErrorCode.ClosedByPeer)
            {
                // Expected if the peer also sends a CloseConnection frame and the connection is closed first.
            }

            // When the peer receives the CloseConnection frame, the peer closes the connection. We wait for the
            // connection closure here. We can't just return and close the underlying transport since this could abort
            // the receive of the dispatch responses and close connection frame by the peer.
            await _pendingClose.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

            static void EncodeCloseConnectionFrame(DuplexConnectionWriter writer)
            {
                var encoder = new SliceEncoder(writer, SliceEncoding.Slice1);
                IceDefinitions.CloseConnectionFrame.Encode(ref encoder);
            }
        }
        else
        {
            // We're shutting down an un-connected connection. We just close the underlying transport.
            Debug.Assert(_dispatchCount == 0 && _invocations.Count == 0);
            _duplexConnection.Dispose();
        }
    }

    /// <summary>Creates a pipe reader to simplify the reading of a request or response frame. The frame is read
    /// fully and buffered into an internal pipe.</summary>
    private static async ValueTask<PipeReader> CreateFrameReaderAsync(
        int size,
        DuplexConnectionReader transportConnectionReader,
        MemoryPool<byte> pool,
        int minimumSegmentSize,
        CancellationToken cancellationToken)
    {
        var pipe = new Pipe(new PipeOptions(
            pool: pool,
            minimumSegmentSize: minimumSegmentSize,
            pauseWriterThreshold: 0,
            writerScheduler: PipeScheduler.Inline));

        try
        {
            await transportConnectionReader.FillBufferWriterAsync(
                pipe.Writer,
                size,
                cancellationToken).ConfigureAwait(false);
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
        CancellationToken cancellationToken)
    {
        // We use ReadAtLeastAsync instead of ReadAsync to bypass the PauseWriterThreshold when the payload is
        // backed by a Pipe.
        ReadResult readResult = await payload.ReadAtLeastAsync(int.MaxValue, cancellationToken).ConfigureAwait(false);

        if (readResult.IsCanceled)
        {
            throw new InvalidOperationException("unexpected call to CancelPendingRead on ice payload");
        }

        return readResult.IsCompleted ? readResult.Buffer :
            throw new ArgumentException("the payload size is greater than int.MaxValue", nameof(payload));
    }

    /// <summary>Read incoming frames and returns on graceful connection shutdown.</summary>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    private async ValueTask ReadFramesAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            ReadOnlySequence<byte> buffer = await _duplexConnectionReader.ReadAtLeastAsync(
                IceDefinitions.PrologueSize,
                cancellationToken).ConfigureAwait(false);

            // First decode and check the prologue.

            ReadOnlySequence<byte> prologueBuffer = buffer.Slice(0, IceDefinitions.PrologueSize);

            IcePrologue prologue = SliceEncoding.Slice1.DecodeBuffer(
                prologueBuffer,
                (ref SliceDecoder decoder) => new IcePrologue(ref decoder));

            _duplexConnectionReader.AdvanceTo(prologueBuffer.End);

            IceDefinitions.CheckPrologue(prologue);
            if (prologue.FrameSize > _maxFrameSize)
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
                    return;
                }

                case IceFrameType.Request:
                    await ReadRequestAsync(prologue.FrameSize).ConfigureAwait(false);
                    break;

                case IceFrameType.RequestBatch:
                    // Read and ignore
                    PipeReader batchRequestReader = await CreateFrameReaderAsync(
                        prologue.FrameSize - IceDefinitions.PrologueSize,
                        _duplexConnectionReader,
                        _memoryPool,
                        _minSegmentSize,
                        cancellationToken).ConfigureAwait(false);
                    await batchRequestReader.CompleteAsync().ConfigureAwait(false);
                    break;

                case IceFrameType.Reply:
                    await ReadReplyAsync(prologue.FrameSize).ConfigureAwait(false);
                    break;

                case IceFrameType.ValidateConnection:
                {
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
                _duplexConnectionReader,
                _memoryPool,
                _minSegmentSize,
                cancellationToken).ConfigureAwait(false);

            bool cleanupFrameReader = true;

            try
            {
                // Read and decode request ID
                if (!replyFrameReader.TryRead(out ReadResult readResult) || readResult.Buffer.Length < 4)
                {
                    throw new InvalidDataException("received invalid response request ID");
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
                    else if (!_isReadOnly)
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
                _duplexConnectionReader,
                _memoryPool,
                _minSegmentSize,
                cancellationToken).ConfigureAwait(false);

            // Decode its header.
            int requestId;
            IceRequestHeader requestHeader;
            PipeReader? contextReader = null;
            try
            {
                if (!requestFrameReader.TryRead(out ReadResult readResult))
                {
                    throw new InvalidDataException("received invalid request frame");
                }

                Debug.Assert(readResult.IsCompleted);

                (requestId, requestHeader, contextReader, int consumed) = DecodeRequestIdAndHeader(readResult.Buffer);
                requestFrameReader.AdvanceTo(readResult.Buffer.GetPosition(consumed));

                IDictionary<RequestFieldKey, ReadOnlySequence<byte>>? fields;
                if (contextReader is null)
                {
                    fields = requestHeader.OperationMode == OperationMode.Normal ?
                        ImmutableDictionary<RequestFieldKey, ReadOnlySequence<byte>>.Empty : _idempotentFields;
                }
                else
                {
                    contextReader.TryRead(out ReadResult result);
                    Debug.Assert(result.Buffer.Length > 0 && result.IsCompleted);
                    fields = new Dictionary<RequestFieldKey, ReadOnlySequence<byte>>()
                    {
                        [RequestFieldKey.Context] = result.Buffer
                    };

                    if (requestHeader.OperationMode != OperationMode.Normal)
                    {
                        // OperationMode can be Idempotent or Nonmutating.
                        fields[RequestFieldKey.Idempotent] = default;
                    }
                }

                if (_dispatchSemaphore is SemaphoreSlim dispatchSemaphore)
                {
                    // This prevents us from receiving any new frames if we're already dispatching the maximum number of
                    // requests. We need to do this in the "accept from network loop" to apply back pressure to the caller.
                    try
                    {
                        await dispatchSemaphore.WaitAsync(_dispatchesAndInvocationsCts.Token)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        Debug.Assert(_isReadOnly);
                    }
                }

                lock (_mutex)
                {
                    if (_isReadOnly)
                    {
                        Debug.Assert(ConnectionClosedException is not null);
                        throw ConnectionClosedException;
                    }
                    else if (_invocations.Count == 0 && ++_dispatchCount == 1)
                    {
                        // We were idle, we no longer are.
                        DisableIdleCheck();
                    }
                }

                // The scheduling of the task can't be canceled since we want to make sure DispatchRequestAsync will
                // cleanup the dispatch if DisposeAsync is called.
                _ = Task.Run(
                    async () =>
                    {
                        using var request = new IncomingRequest(_connectionContext!)
                        {
                            Fields = fields,
                            Fragment = requestHeader.Fragment,
                            IsOneway = requestId == 0,
                            Operation = requestHeader.Operation,
                            Path = requestHeader.Path,
                            Payload = requestFrameReader,
                        };
                        await DispatchRequestAsync(request, contextReader).ConfigureAwait(false);
                    },
                    CancellationToken.None);
            }
            catch
            {
                // If shutting down or aborted, ignore the incoming request.
                await requestFrameReader.CompleteAsync(ConnectionClosedException).ConfigureAwait(false);
                if (contextReader is not null)
                {
                    await contextReader.CompleteAsync().ConfigureAwait(false);
                }
                throw;
            }

            async Task DispatchRequestAsync(IncomingRequest request, PipeReader? contextReader)
            {
                OutgoingResponse? response = null;
                try
                {
                    // The dispatcher can complete the incoming request payload to release its memory as soon as
                    // possible.
                    try
                    {
                        response = await _dispatcher.DispatchAsync(
                            request,
                            _dispatchesAndInvocationsCts.Token).ConfigureAwait(false);
                    }
                    finally
                    {
                        _dispatchSemaphore?.Release();
                    }

                    if (response != request.Response)
                    {
                        var exception = new InvalidOperationException(
                            "the dispatcher did not return the last response created for this request");

                        await response.Payload.CompleteAsync(exception).ConfigureAwait(false);
                        if (response.PayloadStream is PipeReader payloadStream)
                        {
                            await payloadStream.CompleteAsync(exception).ConfigureAwait(false);
                        }
                        throw exception;
                    }
                }
                catch when (request.IsOneway)
                {
                    // ignored since we're not returning anything
                }
                catch (OperationCanceledException exception) when
                    (exception.CancellationToken == _dispatchesAndInvocationsCts.Token)
                {
                    response = new OutgoingResponse(request)
                    {
                        Payload = CreateExceptionPayload(
                            new DispatchException("dispatch canceled", DispatchErrorCode.Canceled),
                            request),
                        ResultType = ResultType.Failure
                    };
                }
                catch (Exception exception)
                {
                    // If we catch an exception, we return a failure response with a Slice-encoded payload.
                    if (exception is not DispatchException dispatchException || dispatchException.ConvertToUnhandled)
                    {
                        DispatchErrorCode errorCode = exception switch
                        {
                            InvalidDataException _ => DispatchErrorCode.InvalidData,
                            _ => DispatchErrorCode.UnhandledException
                        };

                        // We pass null for message to get the message computed by DispatchException.DefaultMessage.
                        dispatchException = new DispatchException(
                            message: null,
                            errorCode,
                            _includeInnerExceptionDetails ? exception : null);
                    }

                    response = new OutgoingResponse(request)
                    {
                        Payload = CreateExceptionPayload(dispatchException, request),
                        ResultType = ResultType.Failure
                    };
                }
                finally
                {
                    await request.Payload.CompleteAsync().ConfigureAwait(false);
                    if (contextReader is not null)
                    {
                        await contextReader.CompleteAsync().ConfigureAwait(false);

                        // The field values are now invalid - they point to potentially recycled and reused memory. We
                        // replace Fields by an empty dictionary to prevent accidental access to this reused memory.
                        request.Fields = ImmutableDictionary<RequestFieldKey, ReadOnlySequence<byte>>.Empty;
                    }
                }

                PipeWriter payloadWriter = _payloadWriter;
                bool acquiredSemaphore = false;
                Exception? completeException = null;

                try
                {
                    if (request.IsOneway)
                    {
                        return;
                    }

                    Debug.Assert(response is not null);

                    if (response.PayloadStream is not null)
                    {
                        throw new NotSupportedException("PayloadStream must be null with the ice protocol");
                    }

                    // Read the full payload. This can take some time so this needs to be done before acquiring the
                    // write semaphore.
                    ReadOnlySequence<byte> payload = await ReadFullPayloadAsync(
                        response.Payload,
                        cancellationToken).ConfigureAwait(false);
                    int payloadSize = checked((int)payload.Length);

                    // Wait for writing of other frames to complete. The semaphore is used as an asynchronous queue
                    // to serialize the writing of frames.
                    await _writeSemaphore.EnterAsync(cancellationToken).ConfigureAwait(false);
                    acquiredSemaphore = true;

                    ReplyStatus replyStatus = ReplyStatus.Ok;

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

                    EncodeResponseHeader(_duplexConnectionWriter, requestId, payloadSize, replyStatus);

                    payloadWriter = response.GetPayloadWriter(payloadWriter);

                    // Write the payload and complete the source.
                    FlushResult flushResult = await payloadWriter.WriteAsync(
                        payload,
                        endStream: false,
                        cancellationToken).ConfigureAwait(false);

                    // If a payload writer decorator returns a canceled or completed flush result, we have to throw
                    // NotSupportedException. We can't interrupt the writing of a payload since it would lead to a
                    // bogus payload to be sent over the connection.
                    if (flushResult.IsCanceled || flushResult.IsCompleted)
                    {
                        throw new NotSupportedException(
                            "payload writer cancellation or completion is not supported with the ice protocol");
                    }
                }
                catch (Exception exception)
                {
                    completeException = exception;
                }
                finally
                {
                    await payloadWriter.CompleteAsync(completeException).ConfigureAwait(false);

                    if (acquiredSemaphore)
                    {
                        _writeSemaphore.Release();
                    }

                    lock (_mutex)
                    {
                        // Dispatch is done.
                        --_dispatchCount;
                        if (_invocations.Count == 0 && _dispatchCount == 0)
                        {
                            if (_isReadOnly)
                            {
                                _dispatchesAndInvocationsCompleted.TrySetResult();
                            }
                            else
                            {
                                EnableIdleCheck();
                            }
                        }
                    }
                }

                static PipeReader CreateExceptionPayload(DispatchException dispatchException, IncomingRequest request)
                {
                    SliceEncodeOptions encodeOptions = request.Features.Get<ISliceFeature>()?.EncodeOptions ??
                        SliceEncodeOptions.Default;

                    var pipe = new Pipe(encodeOptions.PipeOptions);

                    var encoder = new SliceEncoder(pipe.Writer, SliceEncoding.Slice1);
                    encoder.EncodeSystemException(
                        dispatchException,
                        request.Path,
                        request.Fragment,
                        request.Operation);
                    pipe.Writer.Complete(); // flush to reader and sets Is[Writer]Completed to true.
                    return pipe.Reader;
                }

                static void EncodeResponseHeader(
                    DuplexConnectionWriter writer,
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

            static (int RequestId, IceRequestHeader Header, PipeReader? ContextReader, int Consumed) DecodeRequestIdAndHeader(
                ReadOnlySequence<byte> buffer)
            {
                var decoder = new SliceDecoder(buffer, SliceEncoding.Slice1);

                int requestId = decoder.DecodeInt32();

                var requestHeader = new IceRequestHeader(ref decoder);

                Pipe? contextPipe = null;
                long pos = decoder.Consumed;
                int count = decoder.DecodeSize();
                if (count > 0)
                {
                    for (int i = 0; i < count; ++i)
                    {
                        decoder.Skip(decoder.DecodeSize()); // Skip the key
                        decoder.Skip(decoder.DecodeSize()); // Skip the value
                    }
                    contextPipe = new Pipe();
                    contextPipe.Writer.Write(buffer.Slice(pos, decoder.Consumed - pos));
                    contextPipe.Writer.Complete();
                }

                var encapsulationHeader = new EncapsulationHeader(ref decoder);

                if (encapsulationHeader.PayloadEncodingMajor != 1 ||
                    encapsulationHeader.PayloadEncodingMinor != 1)
                {
                    throw new InvalidDataException(
                        $"unsupported payload encoding '{encapsulationHeader.PayloadEncodingMajor}.{encapsulationHeader.PayloadEncodingMinor}'");
                }

                int payloadSize = encapsulationHeader.EncapsulationSize - 6;
                if (payloadSize != (buffer.Length - decoder.Consumed))
                {
                    throw new InvalidDataException(
                        $"request payload size mismatch: expected {payloadSize} bytes, read {buffer.Length - decoder.Consumed} bytes");
                }

                return (requestId, requestHeader, contextPipe?.Reader, (int)decoder.Consumed);
            }
        }
    }
}
