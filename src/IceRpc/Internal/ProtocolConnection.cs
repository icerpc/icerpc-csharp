// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System.Diagnostics;

namespace IceRpc.Internal;

/// <summary>The base implementation of <see cref="IProtocolConnection"/>.</summary>
internal abstract class ProtocolConnection : IProtocolConnection
{
    public abstract ServerAddress ServerAddress { get; }

    public Task<string> ShutdownComplete => _shutdownCompleteSource.Task;

    private protected bool IsServer { get; }

    // Derived classes need to be able to set the connection closed exception with their mutex locked. We use an atomic
    // CompareExchange to avoid locking _mutex and to ensure we only set a single exception, the first one.
    private protected ConnectionException? ConnectionClosedException
    {
        get => Volatile.Read(ref _connectionClosedException);
        set
        {
            Debug.Assert(value is not null && value.ErrorCode == ConnectionErrorCode.Closed);
            Interlocked.CompareExchange(ref _connectionClosedException, value, null);
        }
    }

    private ConnectionException? _connectionClosedException;
    private CancellationTokenSource? _connectCts;
    private Task<TransportConnectionInformation>? _connectTask;
    private readonly TimeSpan _connectTimeout;
    private Task? _disposeTask;
    private readonly TimeSpan _idleTimeout;
    private readonly Timer _idleTimeoutTimer;
    private readonly object _mutex = new();

    private readonly TaskCompletionSource<string> _shutdownCompleteSource =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly CancellationTokenSource _shutdownCts = new();
    private Task? _shutdownTask;
    private readonly TimeSpan _shutdownTimeout;

    public Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken)
    {
        lock (_mutex)
        {
            if (_disposeTask is not null)
            {
                Debug.Assert(ConnectionClosedException is not null);
                throw new ObjectDisposedException($"{typeof(ProtocolConnection)}", ConnectionClosedException);
            }
            else if (_shutdownTask is not null)
            {
                Debug.Assert(ConnectionClosedException is not null);
                throw ConnectionClosedException;
            }
            else if (_connectTask is not null)
            {
                throw new InvalidOperationException("unexpected second call to ConnectAsync");
            }
            else
            {
                _connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _connectCts.CancelAfter(_connectTimeout);
                _connectTask = PerformConnectAsync();
            }
        }
        return _connectTask;

        async Task<TransportConnectionInformation> PerformConnectAsync()
        {
            // Make sure we execute the function without holding the connection mutex lock.
            await Task.Yield();

            try
            {
                TransportConnectionInformation information = await ConnectAsyncCore(_connectCts.Token)
                    .ConfigureAwait(false);
                EnableIdleCheck();
                return information;
            }
            catch (OperationCanceledException)
            {
                cancellationToken.ThrowIfCancellationRequested();

                lock (_mutex)
                {
                    Debug.Assert(_disposeTask is null); // DisposeAsync doesn't cancel ConnectAsync.
                    if (_shutdownTask is not null && (_shutdownTask.IsCanceled || _shutdownTask.IsFaulted))
                    {
                        throw new ConnectionException(ConnectionErrorCode.OperationCanceled);
                    }
                    else
                    {
                        throw new TimeoutException(
                            $"connection establishment timed out after {_connectTimeout.TotalSeconds}s");
                    }
                }
            }
            catch (TransportException exception) when (
                exception.ApplicationErrorCode is ulong errorCode &&
                errorCode == (ulong)IceRpcConnectionErrorCode.Refused)
            {
                throw new ConnectionException(ConnectionErrorCode.ConnectRefused);
            }
            catch (TransportException exception)
            {
                throw new ConnectionException(ConnectionErrorCode.TransportError, exception);
            }
            catch (Exception exception)
            {
                throw new ConnectionException(ConnectionErrorCode.Unexpected, exception);
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        // DisposeAsync can be called concurrently. For example, Server can dispose a connection because the client is
        // shutting down and at the same time or shortly after dispose the same connection because of its own disposal.
        // We want to second disposal to "hang" if there is (for example) a bug in the dispatch code that causes the
        // DisposeAsync to hang.
        lock (_mutex)
        {
            _disposeTask ??= PerformDisposeAsync();
        }
        return new(_disposeTask);

        async Task PerformDisposeAsync()
        {
            ConnectionClosedException = new(ConnectionErrorCode.Closed, "the connection was shutdown");

            // Make sure we execute the code below without holding the mutex lock.
            await Task.Yield();

            // We don't lock _mutex since once _disposeTask is not null, _connectTask, _shutdownTask etc are read-only.

            if (_connectTask is null)
            {
                _ = _shutdownCompleteSource.TrySetResult(""); // disposing non-connected connection
            }
            else
            {
                try
                {
                    // Wait for the connection establishment to complete. DisposeAsync performs a graceful shutdown of
                    // the connection so we don't cancel it. Cancelling connection establishment could end up aborting
                    // the connection on the peer if its ConnectAsync completed successfully.
                    _ = await _connectTask.ConfigureAwait(false);

                    if (_shutdownTask is null)
                    {
                        // Perform speedy shutdown.
                        _shutdownTask = CreateShutdownTask(
                            IsServer ? "server connection going away" : "client connection going away",
                            cancelDispatchesAndInvocations: true);
                    }
                    else if (!_shutdownTask.IsCanceled && !_shutdownTask.IsFaulted)
                    {
                        // Speed-up shutdown only if shutdown didn't fail.
                        CancelDispatchesAndInvocations(new ConnectionException(ConnectionErrorCode.OperationCanceled));
                    }

                    try
                    {
                        await _shutdownTask.ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                }
                catch
                {
                    // The connection establishment failed.
                    // TODO: Should _shutdownCompleteSource be completed by ConnectAsync rather than completing it
                    // only when ShutdownAsync or DisposeAsync are called?
                    _ = _shutdownCompleteSource.TrySetException(new ConnectionException(ConnectionErrorCode.Closed));
                }
            }

            await DisposeAsyncCore().ConfigureAwait(false);

            // Clean up disposable resources.
            await _idleTimeoutTimer.DisposeAsync().ConfigureAwait(false);
            _connectCts?.Dispose();
            _shutdownCts.Dispose();
        }
    }

    public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Protocol != ServerAddress.Protocol)
        {
            throw new InvalidOperationException(
                $"cannot send {request.Protocol} request on {ServerAddress.Protocol} connection");
        }

        lock (_mutex)
        {
            if (_disposeTask is not null)
            {
                Debug.Assert(ConnectionClosedException is not null);
                throw new ObjectDisposedException($"{typeof(ProtocolConnection)}", ConnectionClosedException);
            }
            else if (_shutdownTask is not null)
            {
                Debug.Assert(ConnectionClosedException is not null);
                throw ConnectionClosedException;
            }
            else if (_connectTask is null)
            {
                throw new InvalidOperationException("cannot call InvokeAsync before calling ConnectAsync");
            }
        }

        if (_connectTask.IsCompletedSuccessfully)
        {
            return InvokeAsyncCore(request, cancellationToken);
        }
        else if (_connectTask.IsCompleted)
        {
            throw new InvalidOperationException("cannot call InvokeAsync after ConnectAsync failed");
        }
        else
        {
            return IsServer ? PerformInvokeAsync() :
                throw new InvalidOperationException("cannot call InvokeAsync while connecting a client connection");
        }

        async Task<IncomingResponse> PerformInvokeAsync()
        {
            // It's possible to dispatch a request and expose its connection (invoker) before ConnectAsync completes;
            // in this rare case, we wait for _connectTask to complete before calling InvokeAsyncCore.
            _ = await _connectTask.ConfigureAwait(false);
            return await InvokeAsyncCore(request, cancellationToken).ConfigureAwait(false);
        }
    }

    public Task ShutdownAsync(string message, CancellationToken cancellationToken = default)
    {
        lock (_mutex)
        {
            if (_disposeTask is not null)
            {
                Debug.Assert(ConnectionClosedException is not null);
                throw new ObjectDisposedException($"{typeof(ProtocolConnection)}", ConnectionClosedException);
            }
            else if (_connectTask is not null && (_connectTask.IsCanceled || _connectTask.IsFaulted))
            {
                // The connection establishment failed.
                // TODO: Should _shutdownCompleteSource be completed by ConnectAsync rather than completing it
                // only when ShutdownAsync or DisposeAsync are called?
                var exception = new ConnectionException(ConnectionErrorCode.Closed);
                _ = _shutdownCompleteSource.TrySetException(exception);
                throw exception;
            }

            ConnectionClosedException = new(ConnectionErrorCode.Closed, "the connection was shutdown");

            // If cancellation is requested, we cancel shutdown right away. This is useful to ensure that the connection
            // is always aborted by DisposeAsync when calling ShutdownAsync(new CancellationToken(true)).
            if (cancellationToken.IsCancellationRequested)
            {
                var exception = new ConnectionException(ConnectionErrorCode.OperationCanceled);
                _shutdownTask ??= Task.FromException(exception);
                _ = _shutdownCompleteSource.TrySetException(exception);
                _connectCts?.Cancel();
                _shutdownCts.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }
            else
            {
                _shutdownTask ??= CreateShutdownTask(message);
            }
        }

        return PerformWaitForShutdownAsync();

        async Task PerformWaitForShutdownAsync()
        {
            try
            {
                await _shutdownTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (ex.CancellationToken == cancellationToken)
            {
                try
                {
                    _shutdownCts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
                throw;
            }
        }
    }

    internal ProtocolConnection(bool isServer, ConnectionOptions options)
    {
        IsServer = isServer;

        _connectTimeout = options.ConnectTimeout;
        _shutdownTimeout = options.ShutdownTimeout;
        _idleTimeout = options.IdleTimeout;
        _idleTimeoutTimer = new Timer(_ =>
            {
                if (CheckIfIdle())
                {
                    InitiateShutdown("the connection was idle");
                }
            });
    }

    private protected abstract void CancelDispatchesAndInvocations(Exception exception);

    /// <summary>Checks if the connection is idle. If it's idle, the connection implementation should stop accepting new
    /// invocations and dispatches and return <c>true</c> and <c>false</c> otherwise.</summary>
    private protected abstract bool CheckIfIdle();

    private protected abstract Task<TransportConnectionInformation> ConnectAsyncCore(
        CancellationToken cancellationToken);

    private protected void ConnectionLost(Exception exception) =>
        _ = _shutdownCompleteSource.TrySetException(exception);

    private protected void DisableIdleCheck() =>
        _idleTimeoutTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

    private protected abstract ValueTask DisposeAsyncCore();

    private protected void EnableIdleCheck() =>
        _idleTimeoutTimer.Change(_idleTimeout, Timeout.InfiniteTimeSpan);

    /// <summary>Initiate shutdown if it's not already initiated.</summary>
    private protected void InitiateShutdown(string message)
    {
        lock (_mutex)
        {
            if (_disposeTask is not null || _shutdownTask is not null)
            {
                return;
            }

            ConnectionClosedException = new(ConnectionErrorCode.Closed, message);
            _shutdownTask = CreateShutdownTask(message);
        }
    }

    private protected abstract Task<IncomingResponse> InvokeAsyncCore(
        OutgoingRequest request,
        CancellationToken cancellationToken);

    private protected abstract Task ShutdownAsyncCore(string message, CancellationToken cancellationToken);

    private async Task CreateShutdownTask(string message, bool cancelDispatchesAndInvocations = false)
    {
        // Make sure we execute the function without holding the connection mutex lock.
        await Task.Yield();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_shutdownCts.Token);
        cts.CancelAfter(_shutdownTimeout);

        try
        {
            // Wait for connect to complete first.
            if (_connectTask is not null)
            {
                _ = await _connectTask.WaitAsync(cts.Token).ConfigureAwait(false);

                if (cancelDispatchesAndInvocations)
                {
                    CancelDispatchesAndInvocations(new ConnectionException(ConnectionErrorCode.OperationCanceled));
                }
            }

            // Wait for shutdown to complete.
            await ShutdownAsyncCore(message, cts.Token).ConfigureAwait(false);

            _shutdownCompleteSource.SetResult(message);
        }
        catch (OperationCanceledException operationCanceledException)
        {
            Exception exception;

            if (_shutdownCts.IsCancellationRequested)
            {
                exception = new ConnectionException(ConnectionErrorCode.OperationCanceled);
            }
            else
            {
                Debug.Assert(operationCanceledException.CancellationToken == cts.Token);
                exception = new TimeoutException(
                    $"connection shutdown timed out after {_shutdownTimeout.TotalSeconds}s");
            }

            _connectCts?.Cancel();
            _ = _shutdownCompleteSource.TrySetException(exception);
            throw exception;
        }
        catch (ConnectionException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var exception = new ConnectionException(
                ex is TransportException ? ConnectionErrorCode.TransportError : ConnectionErrorCode.Unexpected,
                ex);
            _connectCts?.Cancel();
            _ = _shutdownCompleteSource.TrySetException(exception);
            throw exception;
        }
    }
}
