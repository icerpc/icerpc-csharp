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

    private readonly TaskCompletionSource _closeTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
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
    private readonly Action<Exception> _faultedTaskAction;
    private readonly TimeSpan _idleTimeout;
    private int _invocationCount;
    private readonly int _maxFrameSize;
    private readonly MemoryPool<byte> _memoryPool;
    private readonly int _minSegmentSize;
    private readonly object _mutex = new();
    private int _nextRequestId;
    private readonly IcePayloadPipeWriter _payloadWriter;
    private Task _pingTask = Task.CompletedTask;
    private Task? _readFramesTask;
    private readonly CancellationTokenSource _tasksCts = new();
    // Only set for server connections.
    private readonly TransportConnectionInformation? _transportConnectionInformation;
    private readonly Dictionary<int, TaskCompletionSource<PipeReader>> _twowayInvocations = new();
    private readonly AsyncSemaphore _writeSemaphore = new(1, 1);

    internal IceProtocolConnection(
        IDuplexConnection duplexConnection,
        TransportConnectionInformation? transportConnectionInformation,
        ConnectionOptions options)
        : base(isServer: transportConnectionInformation is not null, options)
    {
        // With ice, we always listen for incoming frames (responses) so we need a dispatcher for incoming requests even
        // if we don't expect any. This dispatcher throws an ice ObjectNotExistException back to the client, which makes
        // more sense than throwing an UnknownException.
        _dispatcher = options.Dispatcher ?? ServiceNotFoundDispatcher.Instance;
        _faultedTaskAction = options.FaultedTaskAction;
        _maxFrameSize = options.MaxIceFrameSize;
        _transportConnectionInformation = transportConnectionInformation;

        if (options.MaxDispatches > 0)
        {
            _dispatchSemaphore = new SemaphoreSlim(
                initialCount: options.MaxDispatches,
                maxCount: options.MaxDispatches);
        }

        _idleTimeout = options.IdleTimeout;
        _memoryPool = options.Pool;
        _minSegmentSize = options.MinSegmentSize;

        _duplexConnection = duplexConnection;
        _duplexConnectionWriter = new DuplexConnectionWriter(
            duplexConnection,
            _memoryPool,
            _minSegmentSize,
            keepAliveAction: () =>
            {
                lock (_mutex)
                {
                    if (_pingTask.IsCompleted && !_tasksCts.IsCancellationRequested)
                    {
                        _pingTask = PingAsync(_tasksCts.Token);
                    }
                }
            });
        _duplexConnectionReader = new DuplexConnectionReader(
            duplexConnection,
            _memoryPool,
            _minSegmentSize,
            connectionLostAction: ConnectionClosed);

        _payloadWriter = new IcePayloadPipeWriter(_duplexConnectionWriter);

        async Task PingAsync(CancellationToken cancellationToken)
        {
            Debug.Assert(_duplexConnectionWriter is not null);

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
                catch
                {
                    // Ignore, the read frames task will fail if the connection fails.
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
        Debug.Assert(ConnectionClosedException is not null);

        if (!_dispatchesAndInvocationsCts.IsCancellationRequested)
        {
            // Cancel dispatches and invocations for a speedy shutdown.
            _dispatchesAndInvocationsCts.Cancel();

            lock (_mutex)
            {
                if (_invocationCount == 0 && _dispatchCount == 0)
                {
                    _dispatchesAndInvocationsCompleted.TrySetResult();
                }
            }
        }
    }

    private protected override bool CheckIfIdle()
    {
        // CheckForIdle only checks if the connection is idle. It's the caller that takes action.
        lock (_mutex)
        {
            return _invocationCount == 0 && _dispatchCount == 0;
        }
    }

    private protected override async Task<TransportConnectionInformation> ConnectAsyncCore(
        CancellationToken cancellationToken)
    {
        // If the transport connection information is null, we need to connect the transport connection. It's null for
        // client connections. The transport connection of a server connection is established by Server.
        TransportConnectionInformation transportConnectionInformation =
            _transportConnectionInformation ??
            await _duplexConnection.ConnectAsync(cancellationToken).ConfigureAwait(false);

        // This needs to be set before starting the read frames task below.
        _connectionContext = new ConnectionContext(this, transportConnectionInformation);

        // Enable the idle timeout checks after the transport connection establishment. The sending of keep alive
        // messages requires the connection to be established.
        _duplexConnectionReader.EnableAliveCheck(_idleTimeout);
        _duplexConnectionWriter.EnableKeepAlive(_idleTimeout / 2);

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

                    // The peer expects the connection to be closed once the CloseConnection frame is received.
                    Close("The connection was closed by the peer.");

                    // Notify the ConnectionClosed callback that the connection is now closed.
                    ConnectionClosed();
                }
                catch (Exception exception)
                {
                    // Notify the ConnectionClosed callback if the connection is not already closed.
                    if (ConnectionClosedException is null)
                    {
                        ConnectionClosed(exception);
                    }

                    Close("The connection was lost.", exception);
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
        // Close the transport connection and cancel dispatches and invocations.
        Close();

        // Wait for the read frames and ping tasks to complete.
        await Task.WhenAll(_readFramesTask ?? Task.CompletedTask, _pingTask).ConfigureAwait(false);

        if (_readFramesTask is not null)
        {
            // Wait for dispatches and invocations to complete if the connection is connected.
            await _dispatchesAndInvocationsCompleted.Task.ConfigureAwait(false);
        }

        // It's safe to dispose of the reader/writer since no more threads are sending/receiving data.
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
        CancellationTokenSource cts;

        lock (_mutex)
        {
            // Nothing prevents InvokeAsync to be called on a connection which is no longer accepting invocations. We
            // check for this condition here and throw ConnectionClosedException.
            if (ConnectionClosedException is not null)
            {
                throw ConnectionClosedException;
            }

            if (_invocationCount == 0 && _dispatchCount == 0)
            {
                DisableIdleCheck();
            }
            ++_invocationCount;

            // _dispatchesAndInvocationsCts token can throw ObjectDisposedException so only create the
            // linked source if the connection is not disposed.
            cts = CancellationTokenSource.CreateLinkedTokenSource(
                _dispatchesAndInvocationsCts.Token,
                cancellationToken);
        }

        PipeReader? frameReader = null;
        int requestId = 0;
        try
        {
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
                lock (_mutex)
                {
                    if (ConnectionClosedException is not null)
                    {
                        throw ConnectionClosedException;
                    }

                    if (!request.IsOneway)
                    {
                        requestId = ++_nextRequestId;
                        responseCompletionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
                        _twowayInvocations[requestId] = responseCompletionSource;
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

                request.Payload.Complete();
            }
            finally
            {
                payloadWriter.Complete();
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

            (StatusCode statusCode, string? errorMessage, SequencePosition consumed) =
                DecodeResponseHeader(readResult.Buffer, requestId);

            frameReader.AdvanceTo(consumed);

            var response = new IncomingResponse(
                request,
                _connectionContext!,
                statusCode,
                errorMessage)
            {
                Payload = frameReader
            };

            frameReader = null; // response now owns frameReader
            return response;
        }
        catch (OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Debug.Assert(_dispatchesAndInvocationsCts.IsCancellationRequested || _tasksCts.IsCancellationRequested);

            // The connection is being disposed.
            throw new IceRpcException(IceRpcError.OperationAborted);
        }
        finally
        {
            lock (_mutex)
            {
                // If registered, unregister the twoway invocation.
                if (requestId != 0)
                {
                    _twowayInvocations.Remove(requestId);
                }

                --_invocationCount;
                if (_invocationCount == 0 && _dispatchCount == 0)
                {
                    if (ConnectionClosedException is null)
                    {
                        EnableIdleCheck();
                    }
                    else
                    {
                        _dispatchesAndInvocationsCompleted.TrySetResult();
                    }
                }
            }

            frameReader?.Complete();
            cts.Dispose();
        }

        static (StatusCode StatusCode, string? ErrorMessage, SequencePosition Consumed) DecodeResponseHeader(
            ReadOnlySequence<byte> buffer,
            int requestId)
        {
            ReplyStatus replyStatus = ((int)buffer.FirstSpan[0]).AsReplyStatus();

            if (replyStatus <= ReplyStatus.UserException)
            {
                const int headerSize = 7; // reply status byte + encapsulation header

                // read and check encapsulation header (6 bytes long)

                if (buffer.Length < headerSize)
                {
                    throw new InvalidDataException($"received invalid frame header for request #{requestId}");
                }

                EncapsulationHeader encapsulationHeader = SliceEncoding.Slice1.DecodeBuffer(
                    buffer.Slice(1, 6),
                    (ref SliceDecoder decoder) => new EncapsulationHeader(ref decoder));

                // Sanity check
                int payloadSize = encapsulationHeader.EncapsulationSize - 6;
                if (payloadSize != buffer.Length - headerSize)
                {
                    throw new InvalidDataException(
                        $"response payload size/frame size mismatch: payload size is {payloadSize} bytes but frame has {buffer.Length - headerSize} bytes left");
                }

                SequencePosition consumed = buffer.GetPosition(headerSize);

                return replyStatus == ReplyStatus.Ok ? (StatusCode.Success, null, consumed) :
                    // Set the error message to the empty string. We will convert this empty string to null when we
                    // decode the exception.
                    (StatusCode.ApplicationError, "", consumed);
            }
            else
            {
                // An ice system exception.

                StatusCode statusCode = replyStatus switch
                {
                    ReplyStatus.ObjectNotExistException => StatusCode.ServiceNotFound,
                    ReplyStatus.FacetNotExistException => StatusCode.ServiceNotFound,
                    ReplyStatus.OperationNotExistException => StatusCode.OperationNotFound,
                    _ => StatusCode.UnhandledException
                };

                var decoder = new SliceDecoder(buffer.Slice(1), SliceEncoding.Slice1);

                string message;
                switch (replyStatus)
                {
                    case ReplyStatus.FacetNotExistException:
                    case ReplyStatus.ObjectNotExistException:
                    case ReplyStatus.OperationNotExistException:

                        var requestFailed = new RequestFailedExceptionData(ref decoder);

                        string target = requestFailed.Fragment.Length > 0 ?
                            $"{requestFailed.Path}#{requestFailed.Fragment}" : requestFailed.Path;

                        message = $"The dispatch failed with status code {statusCode} while dispatching '{requestFailed.Operation}' on '{target}'.";
                        break;
                    default:
                        message = decoder.DecodeString();
                        break;
                }

                decoder.CheckEndOfBuffer(skipTaggedParams: false);
                return (statusCode, message, buffer.End);
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
            Debug.Assert(ConnectionClosedException is not null);
            if (_invocationCount == 0 && _dispatchCount == 0)
            {
                _dispatchesAndInvocationsCompleted.TrySetResult();
            }
        }

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
        catch (IceRpcException exception) when (exception.IceRpcError == IceRpcError.OperationAborted)
        {
            // The transport connection is disposed if the peer also sent a CloseConnection frame.
            Debug.Assert(ConnectionClosedException!.IceRpcError == IceRpcError.ConnectionClosed);
        }

        // When the peer receives the CloseConnection frame, the peer closes the connection. We wait for the connection
        // closure here. We can't just return and close the underlying transport since this could abort the receive of
        // the responses and close connection frame by the peer.
        await _closeTcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

        static void EncodeCloseConnectionFrame(DuplexConnectionWriter writer)
        {
            var encoder = new SliceEncoder(writer, SliceEncoding.Slice1);
            IceDefinitions.CloseConnectionFrame.Encode(ref encoder);
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
            pipe.Reader.Complete();
            throw;
        }
        finally
        {
            pipe.Writer.Complete();
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

    /// <summary>Closes the transport connection and cancels pending dispatches and invocations.</summary>
    private void Close(string? message = null, Exception? innerException = null)
    {
        ConnectionClosedException = new IceRpcException(IceRpcError.ConnectionClosed, message, innerException);

        // Cancel tasks that rely on the transport to ensure that no more calls on the transport are pending before
        // calling Dispose.
        _tasksCts.Cancel();

        // Dispose the transport connection. This will abort the transport connection if it wasn't shutdown first.
        _duplexConnection.Dispose();

        // Cancel dispatches and invocations that might still be in progress.
        CancelDispatchesAndInvocations();

        // Make sure to unblock ShutdownAsync if it's waiting for the connection closure.
        _closeTcs.TrySetResult();
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
                    batchRequestReader.Complete();
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

            bool completeFrameReader = true;

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
                    if (_twowayInvocations.TryGetValue(
                        requestId,
                        out TaskCompletionSource<PipeReader>? responseCompletionSource))
                    {
                        responseCompletionSource.SetResult(replyFrameReader);
                        completeFrameReader = false;
                    }
                    else
                    {
                        throw new InvalidDataException("received ice Reply for unknown invocation");
                    }
                }
            }
            finally
            {
                if (completeFrameReader)
                {
                    replyFrameReader.Complete();
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
            IDictionary<RequestFieldKey, ReadOnlySequence<byte>>? fields;

            try
            {
                if (!requestFrameReader.TryRead(out ReadResult readResult))
                {
                    throw new InvalidDataException("received invalid request frame");
                }

                Debug.Assert(readResult.IsCompleted);

                (requestId, requestHeader, contextReader, int consumed) = DecodeRequestIdAndHeader(readResult.Buffer);
                requestFrameReader.AdvanceTo(readResult.Buffer.GetPosition(consumed));

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
                    // This prevents us from receiving any new frames if we're already dispatching the maximum number
                    // of requests. We need to do this in the "accept from network loop" to apply back pressure to the
                    // caller.
                    try
                    {
                        await dispatchSemaphore.WaitAsync(_dispatchesAndInvocationsCts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        Debug.Assert(ConnectionClosedException is not null);
                        throw ConnectionClosedException;
                    }
                }

                lock (_mutex)
                {
                    if (ConnectionClosedException is not null)
                    {
                        throw ConnectionClosedException;
                    }

                    if (_invocationCount == 0 && _dispatchCount == 0)
                    {
                        DisableIdleCheck();
                    }
                    ++_dispatchCount;
                }
            }
            catch
            {
                requestFrameReader.Complete();
                contextReader?.Complete();
                throw;
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
                    try
                    {
                        await DispatchRequestAsync(request, contextReader).ConfigureAwait(false);
                    }
                    catch (IceRpcException exception) when (exception.IceRpcError == IceRpcError.OperationAborted)
                    {
                        // This can occur when the connection is disposed while we're sending a response.
                    }
                    catch (Exception exception)
                    {
                        _faultedTaskAction(exception);
                        throw;
                    }
                },
                CancellationToken.None);

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
                        throw new InvalidOperationException(
                            "the dispatcher did not return the last response created for this request");
                    }
                }
                catch when (request.IsOneway)
                {
                    // ignored since we're not returning anything
                }
                catch (OperationCanceledException exception) when
                    (exception.CancellationToken == _dispatchesAndInvocationsCts.Token)
                {
                    response = new OutgoingResponse(
                        request,
                        StatusCode.UnhandledException,
                        "dispatch canceled");
                }
                catch (Exception exception)
                {
                    // If we catch an exception, we return a system exception.
                    if (exception is not DispatchException dispatchException || dispatchException.ConvertToUnhandled)
                    {
                        // We want the default error message for this new exception.
                        dispatchException =
                            new DispatchException(StatusCode.UnhandledException, message: null, exception);
                    }

                    response = new OutgoingResponse(request, dispatchException);
                }
                finally
                {
                    request.Payload.Complete();
                    contextReader?.Complete();

                    // The field values are now invalid - they point to potentially recycled and reused memory. We
                    // replace Fields by an empty dictionary to prevent accidental access to this reused memory.
                    request.Fields = ImmutableDictionary<RequestFieldKey, ReadOnlySequence<byte>>.Empty;
                }

                PipeWriter payloadWriter = _payloadWriter;
                bool acquiredSemaphore = false;

                try
                {
                    if (request.IsOneway)
                    {
                        return;
                    }

                    Debug.Assert(response is not null);

                    // Read the full payload. This can take some time so this needs to be done before acquiring the
                    // write semaphore.
                    ReadOnlySequence<byte> payload = ReadOnlySequence<byte>.Empty;

                    if (response.StatusCode <= StatusCode.ApplicationError)
                    {
                        try
                        {
                            payload = await ReadFullPayloadAsync(response.Payload, cancellationToken)
                                .ConfigureAwait(false);
                        }
                        catch (Exception exception)
                        {
                            var dispatchException = new DispatchException(
                                StatusCode.UnhandledException,
                                message: null,
                                exception);

                            response = new OutgoingResponse(request, dispatchException);
                        }
                    }
                    // else payload remains empty because the payload of a dispatch exception (if any) cannot be sent
                    // over ice.

                    int payloadSize = checked((int)payload.Length);

                    // Wait for writing of other frames to complete. The semaphore is used as an asynchronous queue
                    // to serialize the writing of frames.
                    await _writeSemaphore.EnterAsync(cancellationToken).ConfigureAwait(false);
                    acquiredSemaphore = true;

                    EncodeResponseHeader(_duplexConnectionWriter, response, request, requestId, payloadSize);

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
                            "A payload writer must not return a completed or canceled FlushResult with the ice protocol.");
                    }
                }
                catch (OperationCanceledException exception) when (
                    exception.CancellationToken == cancellationToken)
                {
                    // expected
                }
                finally
                {
                    payloadWriter.Complete();

                    if (acquiredSemaphore)
                    {
                        _writeSemaphore.Release();
                    }

                    lock (_mutex)
                    {
                        // Dispatch is done.
                        --_dispatchCount;
                        if (_invocationCount == 0 && _dispatchCount == 0)
                        {
                            if (ConnectionClosedException is null)
                            {
                                EnableIdleCheck();
                            }
                            else
                            {
                                _dispatchesAndInvocationsCompleted.TrySetResult();
                            }
                        }
                    }
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

            static void EncodeResponseHeader(
                DuplexConnectionWriter writer,
                OutgoingResponse response,
                IncomingRequest request,
                int requestId,
                int payloadSize)
            {
                var encoder = new SliceEncoder(writer, SliceEncoding.Slice1);

                // Write the response header.

                encoder.WriteByteSpan(IceDefinitions.FramePrologue);
                encoder.EncodeIceFrameType(IceFrameType.Reply);
                encoder.EncodeUInt8(0); // compression status
                Span<byte> sizePlaceholder = encoder.GetPlaceholderSpan(4);

                encoder.EncodeInt32(requestId);

                if (response.StatusCode > StatusCode.ApplicationError ||
                    (response.StatusCode == StatusCode.ApplicationError && payloadSize == 0))
                {
                    // system exception
                    switch (response.StatusCode)
                    {
                        case StatusCode.ServiceNotFound:
                        case StatusCode.OperationNotFound:
                            encoder.EncodeReplyStatus(response.StatusCode == StatusCode.ServiceNotFound ?
                                ReplyStatus.ObjectNotExistException : ReplyStatus.OperationNotExistException);

                            new RequestFailedExceptionData(request.Path, request.Fragment, request.Operation)
                                .Encode(ref encoder);
                            break;
                        case StatusCode.UnhandledException:
                            encoder.EncodeReplyStatus(ReplyStatus.UnknownException);
                            encoder.EncodeString(response.ErrorMessage!);
                            break;
                        default:
                            encoder.EncodeReplyStatus(ReplyStatus.UnknownException);
                            encoder.EncodeString(
                                $"{response.ErrorMessage} {{ Original StatusCode = {response.StatusCode} }}");
                            break;
                    }
                }
                else
                {
                    encoder.EncodeReplyStatus((ReplyStatus)response.StatusCode);

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

                int frameSize = encoder.EncodedByteCount + payloadSize;
                SliceEncoder.EncodeInt32(frameSize, sizePlaceholder);
            }
        }
    }
}
