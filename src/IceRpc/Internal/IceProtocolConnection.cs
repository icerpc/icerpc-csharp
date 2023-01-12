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

internal sealed class IceProtocolConnection : IProtocolConnection
{
    public Task<Exception?> Closed => _closedTcs.Task;

    public ServerAddress ServerAddress => _duplexConnection.ServerAddress;

    public Task ShutdownRequested => _shutdownRequestedTcs.Task;

    private static readonly IDictionary<RequestFieldKey, ReadOnlySequence<byte>> _idempotentFields =
        new Dictionary<RequestFieldKey, ReadOnlySequence<byte>>
        {
            [RequestFieldKey.Idempotent] = default
        }.ToImmutableDictionary();

    private bool IsServer => _transportConnectionInformation is not null;

    private readonly TaskCompletionSource<Exception?> _closedTcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private IConnectionContext? _connectionContext; // non-null once the connection is established
    private Task<TransportConnectionInformation>? _connectTask;
    private readonly IDispatcher _dispatcher;
    private int _dispatchCount;
    private readonly TaskCompletionSource _dispatchesAndInvocationsCompleted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly CancellationTokenSource _dispatchesAndInvocationsCts = new();
    private readonly SemaphoreSlim? _dispatchSemaphore;

    // This cancellation token source is canceled when the connection is disposed.
    private readonly CancellationTokenSource _disposedCts = new();

    private Task? _disposeTask;
    private readonly IDuplexConnection _duplexConnection;
    private readonly DuplexConnectionReader _duplexConnectionReader;
    private readonly DuplexConnectionWriter _duplexConnectionWriter;
    private readonly Action<Exception> _faultedTaskAction;
    private readonly TimeSpan _idleTimeout;
    private readonly Timer _idleTimeoutTimer;
    private int _invocationCount;
    private string? _invocationRefusedMessage;
    private bool _isShutdown;
    private readonly int _maxFrameSize;
    private readonly MemoryPool<byte> _memoryPool;
    private readonly int _minSegmentSize;
    private readonly object _mutex = new();
    private int _nextRequestId;
    private Task _pingTask = Task.CompletedTask;
    private Task? _readFramesTask;
    private volatile bool _receivedCloseConnection;

    // A connection refuses invocations when it's disposed, shut down, shutting down or merely "shutdown requested".
    private bool _refuseInvocations;

    private Task? _shutdownTask;

    // The thread that completes this TCS can run the continuations, and as a result its result must be set without
    // holding a lock on _mutex.
    private readonly TaskCompletionSource _shutdownRequestedTcs = new();

    private readonly TaskCompletionSource _transportConnectionDisposedTcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    // Only set for server connections.
    private readonly TransportConnectionInformation? _transportConnectionInformation;
    private readonly Dictionary<int, TaskCompletionSource<PipeReader>> _twowayInvocations = new();
    private readonly SemaphoreSlim _writeSemaphore = new(1, 1);

    public Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken)
    {
        lock (_mutex)
        {
            if (_disposeTask is not null)
            {
                throw new ObjectDisposedException($"{typeof(IceProtocolConnection)}");
            }
            if (_connectTask is not null)
            {
                throw new InvalidOperationException("Cannot call connect more than once.");
            }

            _connectTask = PerformConnectAsync();
        }
        return _connectTask;

        async Task<TransportConnectionInformation> PerformConnectAsync()
        {
            // Make sure we execute the function without holding the connection mutex lock.
            await Task.Yield();

            // _disposedCts is not disposed at this point because DisposeAsync waits for the completion of _connectTask.
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _disposedCts.Token);

            TransportConnectionInformation transportConnectionInformation;

            try
            {
                // If the transport connection information is null, we need to connect the transport connection. It's
                // null for client connections. The transport connection of a server connection is established by
                // Server.
                transportConnectionInformation = _transportConnectionInformation ??
                    await _duplexConnection.ConnectAsync(connectCts.Token).ConfigureAwait(false);

                if (IsServer)
                {
                    EncodeValidateConnectionFrame(_duplexConnectionWriter);
                    await _duplexConnectionWriter.FlushAsync(connectCts.Token).ConfigureAwait(false);
                }
                else
                {
                    ReadOnlySequence<byte> buffer = await _duplexConnectionReader.ReadAtLeastAsync(
                        IceDefinitions.PrologueSize,
                        connectCts.Token).ConfigureAwait(false);

                    (IcePrologue validateConnectionFrame, long consumed) = DecodeValidateConnectionFrame(buffer);
                    _duplexConnectionReader.AdvanceTo(buffer.GetPosition(consumed), buffer.End);

                    IceDefinitions.CheckPrologue(validateConnectionFrame);
                    if (validateConnectionFrame.FrameSize != IceDefinitions.PrologueSize)
                    {
                        throw new InvalidDataException(
                            $"Received ice frame with only '{validateConnectionFrame.FrameSize}' bytes.");
                    }
                    if (validateConnectionFrame.FrameType != IceFrameType.ValidateConnection)
                    {
                        throw new InvalidDataException(
                            $"Expected '{nameof(IceFrameType.ValidateConnection)}' frame but received frame type '{validateConnectionFrame.FrameType}'.");
                    }
                }
            }
            catch (OperationCanceledException) when (_disposedCts.Token.IsCancellationRequested)
            {
                throw new IceRpcException(
                    IceRpcError.OperationAborted,
                    "The connection establishment was aborted because the connection was disposed.");
            }
            catch (OperationCanceledException)
            {
                Debug.Assert(cancellationToken.IsCancellationRequested);
                var exception = new OperationCanceledException(cancellationToken);
                TryCompleteClosed(exception, "The connection establishment was canceled.");
                throw exception;
            }
            catch (IceRpcException exception)
            {
                TryCompleteClosed(exception, "The connection establishment failed.");
                throw;
            }
            catch (InvalidDataException exception)
            {
                TryCompleteClosed(exception, "The connection establishment failed.");
                throw;
            }
            catch (Exception exception)
            {
                Debug.Fail($"ConnectAsync failed with an unexpected exception: {exception}");
                TryCompleteClosed(exception, "The connection establishment failed.");
                throw;
            }

            // Enable the idle timeout checks after the transport connection establishment. The sending of keep alive
            // messages requires the connection to be established.
            _duplexConnectionReader.EnableAliveCheck(_idleTimeout);
            _duplexConnectionWriter.EnableKeepAlive(_idleTimeout / 2);

            // This needs to be set before starting the read frames task below.
            _connectionContext = new ConnectionContext(this, transportConnectionInformation);

            // We assign _readFramesTask with _mutex locked to make sure this assignment occurs before the start of
            // DisposeAsync. Once _disposeTask is not null, _readFramesTask is immutable.
            lock (_mutex)
            {
                if (_disposeTask is not null)
                {
                    throw new IceRpcException(
                        IceRpcError.OperationAborted,
                        "The connection establishment was aborted because the connection was disposed.");
                }

                _readFramesTask = ReadFramesAsync(_disposedCts.Token);
            }

            EnableIdleCheck();
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
    }

    public ValueTask DisposeAsync()
    {
        lock (_mutex)
        {
            if (_disposeTask is null)
            {
                RefuseNewInvocations("The connection was disposed.");

                if (_invocationCount == 0 && _dispatchCount == 0)
                {
                    _dispatchesAndInvocationsCompleted.TrySetResult();
                }

                _disposeTask = PerformDisposeAsync();
            }
        }
        return new(_disposeTask);

        async Task PerformDisposeAsync()
        {
            // Make sure we execute the code below without holding the mutex lock.
            await Task.Yield();

            _disposedCts.Cancel();
            _dispatchesAndInvocationsCts.Cancel();

            // We don't lock _mutex since once _disposeTask is not null, _connectTask etc are immutable.

            if (_connectTask is null)
            {
                _ = _closedTcs.TrySetResult(null); // disposing non-connected connection
            }
            else
            {
                try
                {
                    await Task.WhenAll(
                        _connectTask,
                        _readFramesTask ?? Task.CompletedTask,
                        _shutdownTask ?? Task.CompletedTask).ConfigureAwait(false);
                }
                catch
                {
                    // Excepted if any of these tasks failed or was canceled. Each task takes care of handling
                    // unexpected exceptions so there's no need to handle them here.
                }

                // We set the result after awaiting _shutdownTask, in case _shutdownTask was still running and about to
                // complete successfully.
                _ = _closedTcs.TrySetResult(
                    new IceRpcException(IceRpcError.OperationAborted, "The connection was disposed."));
            }

            DisposeTransport();

            if (_readFramesTask is not null)
            {
                // Wait for dispatches and invocations to complete if the connection was connected.
                await _dispatchesAndInvocationsCompleted.Task.ConfigureAwait(false);
            }

            // It's safe to dispose of the reader/writer since no more threads are sending/receiving data.
            _duplexConnectionReader.Dispose();
            _duplexConnectionWriter.Dispose();

            _disposedCts.Dispose();
            _dispatchesAndInvocationsCts.Dispose();
            _dispatchSemaphore?.Dispose();
            _writeSemaphore.Dispose();
            await _idleTimeoutTimer.DisposeAsync().ConfigureAwait(false);
        }
    }

    public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Protocol != ServerAddress.Protocol)
        {
            throw new InvalidOperationException(
                $"Cannot send {request.Protocol} request on {ServerAddress.Protocol} connection.");
        }

        lock (_mutex)
        {
            if (_disposeTask is not null)
            {
                throw new ObjectDisposedException($"{typeof(IceRpcProtocolConnection)}");
            }
            if (_refuseInvocations)
            {
                throw new IceRpcException(IceRpcError.InvocationRefused, _invocationRefusedMessage);
            }
            if (_connectTask is null)
            {
                throw new InvalidOperationException("Cannot invoke on a connection before connecting it.");
            }
            if (!IsServer && !_connectTask.IsCompletedSuccessfully)
            {
                throw new InvalidOperationException(
                    "Cannot invoke on a client connection that is not fully established.");
            }
            // It's possible but rare to invoke on a server connection that is still connecting.
        }

        return PerformInvokeAsync();

        async Task<IncomingResponse> PerformInvokeAsync()
        {
            CancellationTokenSource cts;

            lock (_mutex)
            {
                if (_refuseInvocations)
                {
                    throw new IceRpcException(IceRpcError.InvocationRefused, _invocationRefusedMessage);
                }

                if (_invocationCount == 0 && _dispatchCount == 0)
                {
                    DisableIdleCheck();
                }
                ++_invocationCount;

                // Since _refuseInvocations is false, the connection and its _dispatchesAndInvocationsCts token are not
                // disposed.
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
                await _writeSemaphore.WaitAsync(cts.Token).ConfigureAwait(false);
                TaskCompletionSource<PipeReader>? responseCompletionSource = null;

                try
                {
                    // Assign the request ID for twoway invocations and keep track of the invocation for receiving the
                    // response. The request ID is only assigned once the write semaphore is acquired. We don't want a
                    // canceled request to allocate a request ID that won't be used.
                    lock (_mutex)
                    {
                        if (_refuseInvocations)
                        {
                            // It's InvocationCanceled and not InvocationRefused because we've read the payload.
                            throw new IceRpcException(IceRpcError.InvocationCanceled, _invocationRefusedMessage);
                        }

                        if (!request.IsOneway)
                        {
                            requestId = ++_nextRequestId;
                            responseCompletionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
                            _twowayInvocations[requestId] = responseCompletionSource;
                        }
                    }

                    EncodeRequestHeader(_duplexConnectionWriter, request, requestId, payloadSize);

                    await _duplexConnectionWriter.WriteAsync(payload, CancellationToken.None).ConfigureAwait(false);

                    request.Payload.Complete();
                }
                catch (IceRpcException exception) when (exception.IceRpcError != IceRpcError.InvocationCanceled)
                {
                    // Since we could not send the request, the server cannot dispatch it and it's safe to retry.
                    throw new IceRpcException(
                        IceRpcError.InvocationCanceled,
                        "Failed to send ice request.",
                        exception);
                }
                finally
                {
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
                    // If the connection is closed, the WaitAsync might be canceled shortly after the response is received
                    // and before the task completion source continuation is run.
                    frameReader = await responseCompletionSource.Task.ConfigureAwait(false);
                }

                if (!frameReader.TryRead(out ReadResult readResult))
                {
                    throw new InvalidDataException($"Received empty response frame for request with id '{requestId}'.");
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

                // The request was canceled by abortive-shutdown or graceful closure by the peer.
                if (_receivedCloseConnection)
                {
                    throw new IceRpcException(
                        IceRpcError.InvocationCanceled,
                        "The connection was shut down by the peer.");
                }
                else
                {
                    throw new IceRpcException(IceRpcError.OperationAborted);
                }
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
                        if (_isShutdown || _disposeTask is not null)
                        {
                            _dispatchesAndInvocationsCompleted.TrySetResult();
                        }
                        else if (!ShutdownRequested.IsCompleted)
                        {
                            EnableIdleCheck();
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
                        throw new InvalidDataException(
                            $"Received invalid frame header for request with id '{requestId}'.");
                    }

                    EncapsulationHeader encapsulationHeader = SliceEncoding.Slice1.DecodeBuffer(
                        buffer.Slice(1, 6),
                        (ref SliceDecoder decoder) => new EncapsulationHeader(ref decoder));

                    // Sanity check
                    int payloadSize = encapsulationHeader.EncapsulationSize - 6;
                    if (payloadSize != buffer.Length - headerSize)
                    {
                        throw new InvalidDataException(
                            $"Response payload size/frame size mismatch: payload size is {payloadSize} bytes but frame has {buffer.Length - headerSize} bytes left.");
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

                            message =
                                $"The dispatch failed with status code {statusCode} while dispatching '{requestFailed.Operation}' on '{target}'.";
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
    }

    public Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        lock (_mutex)
        {
            if (_disposeTask is not null)
            {
                throw new ObjectDisposedException($"{typeof(IceProtocolConnection)}");
            }
            if (_shutdownTask is not null)
            {
                throw new InvalidOperationException("Cannot call shutdown more than once.");
            }
            if (_connectTask is null)
            {
                throw new InvalidOperationException("Cannot shut down a protocol connection before connecting it.");
            }

            _isShutdown = true;
            RefuseNewInvocations("The connection was shut down.");

            if (_invocationCount == 0 && _dispatchCount == 0)
            {
                _dispatchesAndInvocationsCompleted.TrySetResult();
            }

            _shutdownTask = PerformShutdownAsync();
        }

        return _shutdownTask;

        async Task PerformShutdownAsync()
        {
            await Task.Yield(); // exit mutex lock

            try
            {
                // Wait for connect to complete first.
                _ = await _connectTask.WaitAsync(cancellationToken).ConfigureAwait(false);

                // Since DisposeAsync waits for _shutdownTask completion, _disposedCts is not disposed at this point.
                using var shutdownCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    _disposedCts.Token);

                if (_receivedCloseConnection)
                {
                    DisposeTransport();

                    // Wait for dispatches and invocations to complete.
                    await _dispatchesAndInvocationsCompleted.Task.WaitAsync(shutdownCts.Token).ConfigureAwait(false);
                }
                else
                {
                    // Wait for dispatches and invocations to complete.
                    await _dispatchesAndInvocationsCompleted.Task.WaitAsync(shutdownCts.Token).ConfigureAwait(false);

                    // Encode and write the CloseConnection frame once all the dispatches are done.
                    try
                    {
                        await _writeSemaphore.WaitAsync(shutdownCts.Token).ConfigureAwait(false);
                        try
                        {
                            EncodeCloseConnectionFrame(_duplexConnectionWriter);
                            await _duplexConnectionWriter.FlushAsync(shutdownCts.Token).ConfigureAwait(false);
                        }
                        finally
                        {
                            _writeSemaphore.Release();
                        }

                        // When the peer receives the CloseConnection frame, the peer closes the connection. We wait for the
                        // connection closure here. We can't just return and close the underlying transport since this could
                        // abort the receive of the responses and close connection frame by the peer.
                        await _transportConnectionDisposedTcs.Task.WaitAsync(shutdownCts.Token).ConfigureAwait(false);
                    }
                    catch (IceRpcException exception) when (exception.IceRpcError == IceRpcError.OperationAborted)
                    {
                        // Expected if the flush operation is aborted by connection closure.
                    }
                }

                _closedTcs.SetResult(null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                var exception = new OperationCanceledException(cancellationToken);
                TryCompleteClosed(exception, "The connection shutdown was canceled.");
                throw exception;
            }
            catch (OperationCanceledException)
            {
                lock (_mutex)
                {
                    throw new IceRpcException(
                        IceRpcError.OperationAborted,
                        _disposeTask is null ?
                            "The connection shutdown was aborted because the connection establishment was canceled." :
                            "The connection shutdown was aborted because the connection was disposed.");
                }
            }
            catch (IceRpcException exception)
            {
                TryCompleteClosed(exception, "The connection shutdown failed.");
                throw;
            }
            catch (InvalidDataException exception)
            {
                TryCompleteClosed(exception, "The connection shutdown failed.");
                throw;
            }
            catch (Exception exception)
            {
                Debug.Fail($"ShutdownAsync failed with an unexpected exception: {exception}");
                TryCompleteClosed(exception, "The connection shutdown failed.");
                throw;
            }

            static void EncodeCloseConnectionFrame(DuplexConnectionWriter writer)
            {
                var encoder = new SliceEncoder(writer, SliceEncoding.Slice1);
                IceDefinitions.CloseConnectionFrame.Encode(ref encoder);
            }
        }
    }

    internal IceProtocolConnection(
        IDuplexConnection duplexConnection,
        TransportConnectionInformation? transportConnectionInformation,
        ConnectionOptions options)
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
                    if (_pingTask.IsCompletedSuccessfully && !_isShutdown && _disposeTask is null)
                    {
                        _pingTask = PingAsync(_dispatchesAndInvocationsCts.Token);
                    }
                }
            });
        _duplexConnectionReader = new DuplexConnectionReader(
            duplexConnection,
            _memoryPool,
            _minSegmentSize,
            connectionLostAction: exception => DisposeTransport("The connection was lost.", exception));

        _idleTimeoutTimer = new Timer(_ =>
        {
            bool requestShutdown = false;

            lock (_mutex)
            {
                if (_dispatchCount == 0 && _invocationCount == 0 && !_isShutdown && _disposeTask is null)
                {
                    requestShutdown = true;
                    RefuseNewInvocations(
                        $"The connection was shut down because it was idle for over {_idleTimeout.TotalSeconds} s.");
                    DisableIdleCheck();
                }
            }

            if (requestShutdown)
            {
                // TrySetResult must be called outside the mutex lock
                _shutdownRequestedTcs.TrySetResult();
            }
        });

        async Task PingAsync(CancellationToken cancellationToken)
        {
            Debug.Assert(_duplexConnectionWriter is not null);

            // Make sure we execute the function without holding the connection mutex lock.
            await Task.Yield();

            try
            {
                await _writeSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    EncodeValidateConnectionFrame(_duplexConnectionWriter);
                    await _duplexConnectionWriter.FlushAsync(CancellationToken.None).ConfigureAwait(false);
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
            catch (OperationCanceledException)
            {
                // Ignore, the connection is already closing or closed.
            }
            catch (Exception exception)
            {
                DisposeTransport("The connection failed due to an unhandled exception.", exception);
                Debug.Fail($"The ping task completed due to an unhandled exception: {exception}");
                throw;
            }

            static void EncodeValidateConnectionFrame(DuplexConnectionWriter writer)
            {
                var encoder = new SliceEncoder(writer, SliceEncoding.Slice1);
                IceDefinitions.ValidateConnectionFrame.Encode(ref encoder);
            }
        }
    }

    /// <summary>Creates a pipe reader to simplify the reading of a request or response frame. The frame is read fully
    /// and buffered into an internal pipe.</summary>
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
            throw new InvalidOperationException("Unexpected call to CancelPendingRead on ice payload.");
        }

        return readResult.IsCompleted ? readResult.Buffer :
            throw new ArgumentException("The payload size is greater than int.MaxValue.", nameof(payload));
    }

    private void DisableIdleCheck() => _idleTimeoutTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

    /// <summary>Marks the protocol connection as closed, disposes the transport connection and cancels pending
    /// dispatches and invocations.</summary>
    private void DisposeTransport(string? message = null, Exception? exception = null)
    {
        // The connection can already be refusing invocations if being shutdown or disposed. In this case the connection
        // shutdown or disposal is responsible for completing _closedTcs.
        lock (_mutex)
        {
            if (!_refuseInvocations)
            {
                RefuseNewInvocations(message);
                var rpcException = exception as IceRpcException;
                if (exception is not null && rpcException is null)
                {
                    rpcException = new IceRpcException(IceRpcError.IceRpcError, exception);
                }
                _closedTcs.TrySetResult(rpcException);
            }
        }

        // Dispose the transport connection. This will trigger the failure of tasks waiting on transport operations.
        _duplexConnection.Dispose();

        // Cancel dispatches and invocations, there's no point in letting them continue once the connection is closed.
        _dispatchesAndInvocationsCts.Cancel();

        // Make sure to unblock ShutdownAsync if it's waiting for the connection closure.
        _transportConnectionDisposedTcs.TrySetResult();
    }

    private void EnableIdleCheck() => _idleTimeoutTimer.Change(_idleTimeout, Timeout.InfiniteTimeSpan);

    /// <summary>Reads incoming frames and returns when a CloseConnection frame is received or when the connection is
    /// disposed.</summary>
    /// <remarks>ShutdownAsync does not (and cannot) cancel or otherwise interrupt / await the _readFramesTask.
    /// </remarks>
    private async Task ReadFramesAsync(CancellationToken cancellationToken)
    {
        await Task.Yield(); // exit mutex lock

        try
        {
            while (!cancellationToken.IsCancellationRequested)
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
                        $"Received frame with size ({prologue.FrameSize}) greater than max frame size.");
                }

                if (prologue.CompressionStatus == 2)
                {
                    throw new NotSupportedException("The ice protocol compression is not supported by IceRpc.");
                }

                // Then process the frame based on its type.
                switch (prologue.FrameType)
                {
                    case IceFrameType.CloseConnection:
                    {
                        if (prologue.FrameSize != IceDefinitions.PrologueSize)
                        {
                            throw new InvalidDataException(
                                $"Received {nameof(IceFrameType.CloseConnection)} frame with unexpected data.");
                        }
                        _receivedCloseConnection = true;
                        RefuseNewInvocations(
                            "The connection was shut down because it received a CloseConnection frame from the peer.");
                        _shutdownRequestedTcs.TrySetResult();
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
                                $"Received {nameof(IceFrameType.ValidateConnection)} frame with unexpected data.");
                        }
                        break;
                    }

                    default:
                    {
                        throw new InvalidDataException(
                            $"Received Ice frame with unknown frame type '{prologue.FrameType}'.");
                    }
                }
            } // while
        }
        catch (OperationCanceledException)
        {
            // Expected, the associated cancellation token source was canceled.
        }
        catch (InvalidDataException exception)
        {
            TryCompleteClosed(exception, "The connection was lost.");
        }
        catch (NotSupportedException exception)
        {
            TryCompleteClosed(exception, "The connection was lost.");
        }
        catch (IceRpcException exception)
        {
            DisposeTransport("The connection was lost.", exception);
            // TryCompleteClosed(exception, "The connection was lost.");
        }
        catch (ObjectDisposedException exception)
        {
            // TODO: shouldn't be necessary once DisposeTransport is gone
            TryCompleteClosed(exception, "The connection was lost.");
        }
        catch (Exception exception)
        {
            Debug.Fail($"The read frames task completed due to an unhandled exception: {exception}");
            TryCompleteClosed(exception, "The connection was lost.");
        }

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
                    throw new InvalidDataException("Received a response with an invalid request ID.");
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
                    // else the request ID carried by the response is bogus or corresponds to a request that was
                    // previously discarded (for example, because its deadline expired).
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
            Task? dispatchTask = null;

            try
            {
                if (!requestFrameReader.TryRead(out ReadResult readResult))
                {
                    throw new InvalidDataException("Received an invalid request frame.");
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

                bool releaseDispatchSemaphore = false;
                if (_dispatchSemaphore is SemaphoreSlim dispatchSemaphore)
                {
                    // This prevents us from receiving any new frames if we're already dispatching the maximum number
                    // of requests. We need to do this in the "accept from network loop" to apply back pressure to the
                    // caller.
                    try
                    {
                        await dispatchSemaphore.WaitAsync(_dispatchesAndInvocationsCts.Token).ConfigureAwait(false);
                        releaseDispatchSemaphore = true;
                    }
                    catch (OperationCanceledException)
                    {
                        // and return below
                    }
                }

                lock (_mutex)
                {
                    if (_isShutdown || _disposeTask is not null)
                    {
                        // We're shutting down and we discard this request before any processing. For a graceful
                        // shutdown, we want the invocation in the peer to throw an IceRpcException(
                        // OperationCanceledByShutdown) (meaning no dispatch at all), not a DispatchException (which
                        //  means at least part of the dispatch executed).

                        if (releaseDispatchSemaphore)
                        {
                            _dispatchSemaphore!.Release();
                        }
                        return;
                    }

                    if (_invocationCount == 0 && _dispatchCount == 0)
                    {
                        DisableIdleCheck();
                    }
                    ++_dispatchCount;
                }

                // The scheduling of the task can't be canceled since we want to make sure DispatchRequestAsync will
                // cleanup the dispatch if DisposeAsync is called.
                // dispatchTask takes ownership of the requestFrameReader and contextReader.
                dispatchTask = Task.Run(
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
                        catch (Exception exception)
                        {
                            _faultedTaskAction(exception);
                        }
                    },
                    CancellationToken.None);
            }
            finally
            {
                if (dispatchTask is null)
                {
                    requestFrameReader.Complete();
                    contextReader?.Complete();
                }
            }

            async Task DispatchRequestAsync(IncomingRequest request, PipeReader? contextReader)
            {
                OutgoingResponse? response = null;
                CancellationToken cancellationToken = _dispatchesAndInvocationsCts.Token;
                try
                {
                    // The dispatcher can complete the incoming request payload to release its memory as soon as
                    // possible.
                    try
                    {
                        response = await _dispatcher.DispatchAsync(request, cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        _dispatchSemaphore?.Release();
                    }

                    if (response != request.Response)
                    {
                        throw new InvalidOperationException(
                            "The dispatcher did not return the last response created for this request.");
                    }
                }
                catch when (request.IsOneway)
                {
                    // ignored since we're not returning anything
                }
                catch (OperationCanceledException exception) when (exception.CancellationToken == cancellationToken)
                {
                    response = new OutgoingResponse(
                        request,
                        StatusCode.UnhandledException,
                        "The dispatch was canceled by the closure of the connection.");
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

                    // Wait for writing of other frames to complete. The semaphore is used to serialize the writing of
                    // frames.
                    // We pass CancellationToken.None and not cancellationToken because we want to send the response
                    // even when we're shutting down and cancellationToken is canceled. This can't take forever since
                    // the closure of the transport connection causes the holder of this semaphore to fail and
                    // release it.
                    await _writeSemaphore.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                    acquiredSemaphore = true;

                    EncodeResponseHeader(_duplexConnectionWriter, response, request, requestId, payloadSize);

                    try
                    {
                        // Write the payload and complete the source.
                        await _duplexConnectionWriter.WriteAsync(payload, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (IceRpcException exception) when (
                        exception.IceRpcError is IceRpcError.ConnectionAborted or IceRpcError.OperationAborted)
                    {
                        // The transport connection was disposed, which is ok.
                    }
                }
                finally
                {
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
                            if (_isShutdown || _disposeTask is not null)
                            {
                                _dispatchesAndInvocationsCompleted.TrySetResult();
                            }
                            else if (!ShutdownRequested.IsCompleted)
                            {
                                EnableIdleCheck();
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
                        $"Unsupported payload encoding '{encapsulationHeader.PayloadEncodingMajor}.{encapsulationHeader.PayloadEncodingMinor}'.");
                }

                int payloadSize = encapsulationHeader.EncapsulationSize - 6;
                if (payloadSize != (buffer.Length - decoder.Consumed))
                {
                    throw new InvalidDataException(
                        $"Request payload size mismatch: expected {payloadSize} bytes, read {buffer.Length - decoder.Consumed} bytes.");
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

    private void RefuseNewInvocations(string? message)
    {
        lock (_mutex)
        {
            _refuseInvocations = true;
            _invocationRefusedMessage ??= message;
        }
    }

    private void TryCompleteClosed(Exception exception, string invocationRefusedMessage)
    {
        if (_closedTcs.TrySetResult(exception))
        {
            RefuseNewInvocations(invocationRefusedMessage);
        }
    }
}
