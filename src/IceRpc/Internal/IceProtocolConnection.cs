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

    private readonly TaskCompletionSource<Exception?> _closedTcs = new();

    private IConnectionContext? _connectionContext; // non-null once the connection is established
    private Task<TransportConnectionInformation>? _connectTask;
    private readonly IDispatcher _dispatcher;
    private int _dispatchCount;
    private readonly TaskCompletionSource _dispatchesCompleted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

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

    private readonly TaskCompletionSource _invocationsCompleted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private string? _invocationRefusedMessage;
    private bool _isClosedByPeer;
    private bool _isShutdown;
    private readonly int _maxFrameSize;
    private readonly MemoryPool<byte> _memoryPool;
    private readonly int _minSegmentSize;
    private readonly object _mutex = new();
    private int _nextRequestId;
    private Task _pingTask = Task.CompletedTask;
    private Task? _readFramesTask;

    // A connection refuses invocations when it's disposed, shut down, shutting down or merely "shutdown requested".
    private bool _refuseInvocations;

    private Task? _shutdownTask;

    // The thread that completes this TCS can run the continuations, and as a result its result must be set without
    // holding a lock on _mutex.
    private readonly TaskCompletionSource _shutdownRequestedTcs = new();

    // Only set for server connections.
    private readonly TransportConnectionInformation? _transportConnectionInformation;

    private readonly CancellationTokenSource _twowayDispatchesCts;
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
                // DisposeAsync completes Closed.
                throw new IceRpcException(
                    IceRpcError.OperationAborted,
                    "The connection establishment was aborted because the connection was disposed.");
            }
            // For all other exceptions, we complete _closedTcs. This way the caller (e.g. Server) will typically
            // dispose this failed connection promptly.
            catch (OperationCanceledException)
            {
                Debug.Assert(cancellationToken.IsCancellationRequested);
                var exception = new OperationCanceledException(cancellationToken);
                _ = _closedTcs.TrySetResult(exception);
                throw exception;
            }
            catch (IceRpcException exception)
            {
                _ = _closedTcs.TrySetResult(exception);
                throw;
            }
            catch (InvalidDataException exception)
            {
                _ = _closedTcs.TrySetResult(exception);
                throw;
            }
            catch (Exception exception)
            {
                Debug.Fail($"ConnectAsync failed with an unexpected exception: {exception}");
                _ = _closedTcs.TrySetResult(exception);
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

                // As soon as we exit the mutex lock, _readFramesTask that start dispatching requests and disable this
                // idle check.
                EnableIdleCheck();
            }

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

                _isShutdown = true;

                if (_dispatchCount == 0)
                {
                    _dispatchesCompleted.TrySetResult();
                }
                if (_invocationCount == 0)
                {
                    _invocationsCompleted.TrySetResult();
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

            AbortTwowayInvocations(
                IceRpcError.OperationAborted,
                "The invocation was aborted because the connection was disposed.");

            // We don't lock _mutex since once _disposeTask is not null, _connectTask etc are immutable.

            if (_connectTask is null)
            {
                _ = _closedTcs.TrySetResult(null); // disposing non-connected connection
            }
            else
            {
                // Wait for all tasks to complete, except there is no need to wait for invocations.
                try
                {
                    await Task.WhenAll(
                        _connectTask,
                        _readFramesTask ?? Task.CompletedTask,
                        _pingTask,
                        _dispatchesCompleted.Task,
                        _shutdownTask ?? Task.CompletedTask).ConfigureAwait(false);
                }
                catch
                {
                    // Expected if any of these tasks failed or was canceled. Each task takes care of handling
                    // unexpected exceptions so there's no need to handle them here.
                }

                // We set the result after awaiting _shutdownTask, in case _shutdownTask was still running and about to
                // complete successfully with "SetResult".
                _ = _closedTcs.TrySetResult(
                    new IceRpcException(IceRpcError.OperationAborted, "The connection was disposed."));
            }

            // We dispose the _duplexConnection only after the completion of _readFramesTask. This way, if
            // _readsFrameTask sees a disposed _duplexConnection, it can only be from the idle monitor.
            _duplexConnection.Dispose();

            // It's safe to dispose the reader/writer since no more threads are sending/receiving data.
            _duplexConnectionReader.Dispose();
            _duplexConnectionWriter.Dispose();

            _disposedCts.Dispose();
            _twowayDispatchesCts.Dispose();
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
            if (IsServer)
            {
                // Make sure the connection is fully connected because proceeding.
                try
                {
                    _ = await _connectTask.ConfigureAwait(false);
                }
                catch
                {
                    throw new IceRpcException(
                        IceRpcError.InvocationRefused,
                        "The invocation was refused because the connection establishment failed.");
                }
            }

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
            }

            // Since _invocationCount > 0, _disposedCts is not disposed.
            using var invocationCts =
                CancellationTokenSource.CreateLinkedTokenSource(_disposedCts.Token, cancellationToken);

            PipeReader? frameReader = null;
            TaskCompletionSource<PipeReader>? responseCompletionSource = null;
            int requestId = 0;

            try
            {
                // Read the full payload. This can take some time so this needs to be done before acquiring the write
                // semaphore.
                ReadOnlySequence<byte> payload = await ReadFullPayloadAsync(
                    request.Payload,
                    invocationCts.Token).ConfigureAwait(false);
                int payloadSize = checked((int)payload.Length);

                // Wait for writing of other frames to complete. The semaphore is used as an asynchronous queue to
                // serialize the writing of frames.
                await _writeSemaphore.WaitAsync(invocationCts.Token).ConfigureAwait(false);

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

                            // RunContinuationsAsynchronously because we don't want the "read frames loop" to run the
                            // continuation.
                            responseCompletionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
                            _twowayInvocations[requestId] = responseCompletionSource;
                        }
                    }

                    EncodeRequestHeader(_duplexConnectionWriter, request, requestId, payloadSize);

                    // TODO: see #2472
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
                frameReader = await responseCompletionSource.Task.WaitAsync(invocationCts.Token).ConfigureAwait(false);

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

                lock (_mutex)
                {
                    if (_disposeTask is null)
                    {
                        throw new IceRpcException(
                            IceRpcError.ConnectionAborted,
                            "The invocation was aborted because the connection was aborted.");
                    }
                    else
                    {
                        throw new IceRpcException(
                            IceRpcError.OperationAborted,
                            "The invocation was aborted because the connection was disposed.");
                    }
                }
            }
            finally
            {
                // If responseCompletionSource is not completed, we want to complete it to prevent another method from
                // setting an unobservable exception in it. And if it's already completed with an exception, we observe
                // this exception.
                if (responseCompletionSource is not null &&
                    !responseCompletionSource.TrySetResult(InvalidPipeReader.Instance))
                {
                    try
                    {
                        _ = await responseCompletionSource.Task.ConfigureAwait(false);
                    }
                    catch
                    {
                        // observe exception, if any
                    }
                }

                lock (_mutex)
                {
                    // If registered, unregister the twoway invocation.
                    if (requestId != 0 && !_refuseInvocations)
                    {
                        _twowayInvocations.Remove(requestId);
                    }

                    --_invocationCount;
                    if (_invocationCount == 0)
                    {
                        if (_isShutdown)
                        {
                            _invocationsCompleted.TrySetResult();
                        }
                        else if (_dispatchCount == 0 && !_refuseInvocations)
                        {
                            // We enable the idle check in order to complete ShutdownRequested when idle for too long.
                            // _refuseInvocations is true when the connection is either about to be "shutdown
                            // requested", or shut down / disposed, or aborted (with Closed completed). We don't need to
                            // complete ShutdownRequested in any of these situations.
                            EnableIdleCheck();
                        }
                    }
                }

                frameReader?.Complete();
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

            RefuseNewInvocations("The connection was shut down.");

            _isShutdown = true;
            if (_dispatchCount == 0)
            {
                _dispatchesCompleted.TrySetResult();
            }
            if (_invocationCount == 0)
            {
                _invocationsCompleted.TrySetResult();
            }

            _shutdownTask = PerformShutdownAsync(_isClosedByPeer);
        }

        return _shutdownTask;

        async Task PerformShutdownAsync(bool closedByPeer)
        {
            await Task.Yield(); // exit mutex lock

            try
            {
                // Wait for connect to complete first.
                _ = await _connectTask.WaitAsync(cancellationToken).ConfigureAwait(false);

                // Since DisposeAsync waits for the _shutdownTask completion, _disposedCts is not disposed at this
                // point.
                using var shutdownCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    _disposedCts.Token);

                // Wait for dispatches and invocations to complete.
                await Task.WhenAll(
                    _dispatchesCompleted.Task,
                    _invocationsCompleted.Task).WaitAsync(shutdownCts.Token).ConfigureAwait(false);

                if (!closedByPeer)
                {
                    // Encode and write the CloseConnection frame once all the dispatches are done.
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
                }

                // If closedByPeer is true, _readFramesTask is completed or nearly completed.
                // Otherwise, we've just sent a CloseConnection frame to the peer and we wait for the peer to abort
                // the connection as an acknowledgment for this CloseConnection frame. The peer could also send us
                // a concurrent CloseConnection frame.
                // We can't just return and dispose the duplex connection since this peer can still be reading frames
                // (including the CloseConnection frame) and we don't want to abort this reading.
                await _readFramesTask!.ConfigureAwait(false);

                // It's safe to call SetResult: no other task can (Try)SetResult on _closedTcs at this stage.
                _closedTcs.SetResult(null);

                if (closedByPeer)
                {
                    // The peer is waiting for us to abort the duplex connection; we oblige.
                    _duplexConnection.Dispose();
                }
            }
            // Note that a ShutdownAsync failure does not complete Closed. This way outstanding dispatches and
            // invocations can keep going. Completing Closed typically triggers an immediate disposal by the caller.
            catch (OperationCanceledException)
            {
                cancellationToken.ThrowIfCancellationRequested();

                lock (_mutex)
                {
                    if (_disposeTask is null)
                    {
                        Debug.Assert(_connectTask.IsCanceled);
                        throw new IceRpcException(
                            IceRpcError.ConnectionAborted,
                            "The connection shutdown was aborted because the connection establishment was canceled.");
                    }
                    else
                    {
                        throw new IceRpcException(
                            IceRpcError.OperationAborted,
                            "The connection shutdown was aborted because the connection was disposed.");
                    }
                }
            }
            catch (IceRpcException exception) when (exception.IceRpcError == IceRpcError.OperationAborted)
            {
                // We don't want to throw IceRpcException(OperationAborted) unless this connection is actually
                // disposed.
                lock (_mutex)
                {
                    if (_disposeTask is null)
                    {
                        throw new IceRpcException(IceRpcError.ConnectionAborted, "The connection was aborted.");
                    }
                    else
                    {
                        throw;
                    }
                }
            }
            catch (IceRpcException)
            {
                throw;
            }
            catch (Exception exception)
            {
                Debug.Fail($"ShutdownAsync failed with an unexpected exception: {exception}");
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
        _twowayDispatchesCts = CancellationTokenSource.CreateLinkedTokenSource(_disposedCts.Token);

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
                    if (_pingTask.IsCompletedSuccessfully && _disposeTask is null)
                    {
                        _pingTask = PingAsync(_disposedCts.Token);
                    }
                }
            });
        _duplexConnectionReader = new DuplexConnectionReader(
            duplexConnection,
            _memoryPool,
            _minSegmentSize,
            // TODO: since connectionLostAction always gives the same exception, what's the point of this parameter?
            // We count on the _readFramesTask to report this connection abort (complete Closed etc.). Note this is
            // the only _duplexConnection Dispose that can "interrupt" _readFramesTask.
            connectionLostAction: _ => _duplexConnection.Dispose());

        _idleTimeoutTimer = new Timer(_ =>
        {
            bool requestShutdown = false;

            lock (_mutex)
            {
                if (_dispatchCount == 0 && _invocationCount == 0 && !_isShutdown)
                {
                    requestShutdown = true;
                    RefuseNewInvocations(
                        $"The connection was shut down because it was idle for over {_idleTimeout.TotalSeconds} s.");
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
            catch (OperationCanceledException)
            {
                // Ignore, the connection is already closing or closed.
            }
            catch (Exception exception)
            {
                Debug.Fail($"The ping task completed due to an unhandled exception: {exception}");

                // We don't abort the connection (complete Closed) in this extremely unlikely event (some severe bug
                // with the writeSemaphore). If we did, we would need to await _pingTask in ShutdownAsync.
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

    /// <summary>Aborts all pending twoway invocations.</summary>
    /// <remarks>Must be called outside the mutex lock after setting _refuseInvocations to true.</remarks>
    private void AbortTwowayInvocations(IceRpcError error, string message)
    {
        Debug.Assert(_refuseInvocations);

        // _twowayInvocations is immutable once _refuseInvocations is true.
        foreach (TaskCompletionSource<PipeReader> responseCompletionSource in _twowayInvocations.Values)
        {
            // _twowayInvocations can hold completed completion sources.
            _ = responseCompletionSource.TrySetException(new IceRpcException(error, message));
        }
    }

    private void DisableIdleCheck() => _idleTimeoutTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

    /// <summary>Dispatches an incoming request. This method executes in a task spawn from the read frames loop.
    /// </summary>
    private async Task DispatchRequestAsync(
        IncomingRequest request,
        int requestId,
        PipeReader? contextReader,
        CancellationToken cancellationToken)
    {
        OutgoingResponse? response;
        try
        {
            // The dispatcher can complete the incoming request payload to release its memory as soon as possible.
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
            response = null;
        }
        catch (OperationCanceledException exception) when (exception.CancellationToken == cancellationToken)
        {
            // The connection is disposed or aborted. We're not sending anything back.
            response = null;
        }
        catch (Exception exception)
        {
            // If we catch an exception, we return a system exception.
            if (exception is not DispatchException dispatchException || dispatchException.ConvertToUnhandled)
            {
                // We want the default error message for this new exception.
                dispatchException = new DispatchException(StatusCode.UnhandledException, message: null, exception);
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
            if (response is not null)
            {
                // Read the full response payload. This can take some time so this needs to be done before acquiring
                // the write semaphore.
                ReadOnlySequence<byte> payload = ReadOnlySequence<byte>.Empty;

                if (response.StatusCode <= StatusCode.ApplicationError)
                {
                    try
                    {
                        payload = await ReadFullPayloadAsync(response.Payload, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException exception) when (exception.CancellationToken == cancellationToken)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        // We "encode" the exception in the error message.
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
                await _writeSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                acquiredSemaphore = true;

                EncodeResponseHeader(_duplexConnectionWriter, response, request, requestId, payloadSize);

                try
                {
                    // Write the payload and complete the source. We use _disposedCts.Token here instead of
                    // cancellationToken because canceling _twowayDispatchesCts should not write invalid data.
                    // _disposedCts is not disposed because DisposeAsync waits for dispatches to complete.
                    await _duplexConnectionWriter.WriteAsync(payload, _disposedCts.Token).ConfigureAwait(false);
                }
                catch (IceRpcException exception) when (
                    exception.IceRpcError is IceRpcError.ConnectionAborted or IceRpcError.OperationAborted)
                {
                    // The duplex connection was disposed or aborted, which is ok.
                }
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
                if (_dispatchCount == 0)
                {
                    if (_isShutdown)
                    {
                        _dispatchesCompleted.TrySetResult();
                    }
                    else if (_invocationCount == 0 && !_refuseInvocations)
                    {
                        EnableIdleCheck();
                    }
                }
            }
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

                // When IceRPC receives a response, it ignores the response encoding. So this "1.1" is only relevant to
                // a ZeroC Ice client that decodes the response. The only Slice encoding such a client can possibly use
                // to decode the response payload is 1.1 or 1.0, and we don't care about interop with 1.0.
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

    private void EnableIdleCheck() => _idleTimeoutTimer.Change(_idleTimeout, Timeout.InfiniteTimeSpan);

    /// <summary>Reads incoming frames and returns successfully when a CloseConnection frame is received or when the
    /// connection is closed during ShutdownAsync or canceled by its cancellation token.</summary>
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
                    // The exception handler calls Abort.
                    throw new IceRpcException(
                        IceRpcError.ConnectionAborted,
                        "The connection was aborted because it received a compressed ice frame, and IceRPC does not support ice compression.");
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

                        lock (_mutex)
                        {
                            _isClosedByPeer = true;
                            RefuseNewInvocations(
                                "The connection was shut down because it received a CloseConnection frame from the peer.");

                            // By exiting the "read frames loop" below, we are refusing new dispatches as well.
                        }

                        // Abort twoway invocations that are waiting for a response (it will never come).
                        AbortTwowayInvocations(
                            IceRpcError.InvocationCanceled,
                            "The invocation was canceled by the shutdown of the peer.");

                        // Cancel twoway dispatches since the peer is not interested in the responses. This does not
                        // cancel ongoing writes to _duplexConnection: we don't send incomplete/invalid data.
                        _twowayDispatchesCts.Cancel();

                        // We can't just abort the duplex connection immediately. If we're still writing to it, the
                        // peer could receive invalid data which would make its shutdown fail.

                        // We request a shutdown that will dispose _duplexConnection once all invocations and dispatches
                        // have completed.
                        _shutdownRequestedTcs.TrySetResult();
                        return;
                    }

                    case IceFrameType.Request:
                        await ReadRequestAsync(prologue.FrameSize, cancellationToken).ConfigureAwait(false);
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
                        await ReadReplyAsync(prologue.FrameSize, cancellationToken).ConfigureAwait(false);
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
            // canceled by DisposeAsync, no need to throw anything
        }
        catch (IceRpcException exception) when (
            exception.IceRpcError == IceRpcError.ConnectionAborted &&
            _dispatchesCompleted.Task.IsCompleted && _invocationsCompleted.Task.IsCompleted)
        {
            // The peer acknowledged receipt of the CloseConnection frame by aborting the duplex connection. Return.
            // See ShutdownAsync.
        }
        catch (IceRpcException exception) when (exception.IceRpcError == IceRpcError.OperationAborted)
        {
            // _duplexConnection was disposed by the idle monitor. Nothing else can trigger this exception.
            exception = new IceRpcException(
                IceRpcError.ConnectionIdle,
                "The connection was aborted by the idle monitor.");
            Abort(exception);
            throw exception;
        }
        catch (IceRpcException exception)
        {
            Abort(exception);
            throw;
        }
        catch (InvalidDataException exception)
        {
            var rpcException = new IceRpcException(
                IceRpcError.ConnectionAborted,
                "The connection was aborted by an ice protocol error.",
                exception);

            Abort(rpcException);
            throw rpcException;
        }
        catch (Exception exception)
        {
            Debug.Fail($"The read frames task completed due to an unhandled exception: {exception}");
            Abort(exception);
            throw;
        }

        // Aborts this connection by disposing its underlying duplex connection, aborting twoway invocations and
        // canceling twoway dispatches.
        void Abort(Exception exception)
        {
            RefuseNewInvocations("The connection was lost.");

            _duplexConnection.Dispose();

            AbortTwowayInvocations(
                IceRpcError.ConnectionAborted,
                "The invocation was aborted because the connection was aborted.");

            _twowayDispatchesCts.Cancel();

            // Completing Closed typically triggers an abrupt disposal of the connection, with a different error when
            // aborting twoway invocations. That's why we do it last.
            _ = _closedTcs.TrySetResult(exception);
        }
    }

    /// <summary>Reads a reply (incoming response) and completes the invocation response completion source with this
    /// response. This method executes "synchronously" in the read frames loop.</summary>
    private async Task ReadReplyAsync(int replyFrameSize, CancellationToken cancellationToken)
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
                // The CloseConnection frame completes the read frames task.
                Debug.Assert(!_isClosedByPeer);

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

    /// <summary>Reads and then dispatches an incoming request in a separate dispatch task. This method executes
    /// "synchronously" in the read frames loop.</summary>
    private async Task ReadRequestAsync(int requestFrameSize, CancellationToken cancellationToken)
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
                    await dispatchSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                    releaseDispatchSemaphore = true;
                }
                catch (OperationCanceledException)
                {
                    // and return below
                }
            }

            lock (_mutex)
            {
                if (_isShutdown)
                {
                    // The connection is (being) disposed or the connection is shutting down and received a request.
                    // We simply discard it.
                    // For a graceful shutdown, the twoway invocation in the peer will throw
                    // IceRpcException(InvocationCanceled). We also discard oneway requests: if we accepted them, they
                    // could delay our shutdown and make it time out.
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
            // cleanup (decrement _dispatchCount etc.) if DisposeAsync is called. dispatchTask takes ownership of the
            // requestFrameReader and contextReader.
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

                    CancellationToken cancellationToken = request.IsOneway ?
                        _disposedCts.Token : _twowayDispatchesCts.Token;

                    try
                    {
                        await DispatchRequestAsync(
                            request,
                            requestId,
                            contextReader,
                            cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // expected
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
    }

    private void RefuseNewInvocations(string? message)
    {
        lock (_mutex)
        {
            _refuseInvocations = true;
            _invocationRefusedMessage ??= message;
        }
    }
}
