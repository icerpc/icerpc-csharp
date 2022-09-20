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
    private protected ConnectionClosedException? ConnectionClosedException
    {
        get => Volatile.Read(ref _connectionClosedException);
        set => Interlocked.CompareExchange(ref _connectionClosedException, value, null);
    }

    private ConnectionClosedException? _connectionClosedException;
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
                    if (_disposeTask is not null)
                    {
                        throw new ConnectionAbortedException(ConnectionAbortedErrorCode.Disposed);
                    }
                    else
                    {
                        throw new TimeoutException(
                            $"connection establishment timed out after {_connectTimeout.TotalSeconds}s");
                    }
                }
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
            ConnectionClosedException = new(ConnectionClosedErrorCode.Shutdown);

            // Make sure we execute the code below without holding the mutex lock.
            await Task.Yield();

            // We don't lock _mutex since once _disposeTask is not null, _connectTask, _shutdownTask etc are read-only.

            if (_connectTask is null)
            {
                _ = _shutdownCompleteSource.TrySetResult(""); // disposing non-connected connection
            }
            else
            {
                var connectionAbortedException = new ConnectionAbortedException(ConnectionAbortedErrorCode.Disposed);

                try
                {
                    // Cancel the connection establishment if still in progress.
                    _connectCts!.Cancel();
                    _ = await _connectTask.ConfigureAwait(false);
                }
                catch
                {
                }

                // If connection establishment succeeded, ensure a speedy shutdown.
                if (_connectTask.IsCompletedSuccessfully)
                {
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
                        CancelDispatchesAndInvocations(connectionAbortedException);
                    }

                    try
                    {
                        await _shutdownTask.ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                }
                else
                {
                    _ = _shutdownCompleteSource.TrySetException(connectionAbortedException);
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
                ConnectionAbortedException exception;
                if (_connectTask.IsCanceled)
                {
                    exception = new(ConnectionAbortedErrorCode.ConnectCanceled);
                }
                else
                {
                    exception = new(ConnectionAbortedErrorCode.ConnectFailed, _connectTask.Exception);
                }
                _ = _shutdownCompleteSource.TrySetException(exception);
                throw exception;
            }

            ConnectionClosedException = new(ConnectionClosedErrorCode.Shutdown, message);

            // If cancellation is requested, we cancel shutdown right away. This is useful to ensure that the connection
            // is always aborted by DisposeAsync when calling ShutdownAsync(new CancellationToken(true)).
            if (cancellationToken.IsCancellationRequested)
            {
                var exception = new ConnectionAbortedException(ConnectionAbortedErrorCode.ShutdownCanceled);
                _shutdownTask ??= Task.FromException(exception);
                _ = _shutdownCompleteSource.TrySetException(exception);
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
                    InitiateShutdown("idle connection", ConnectionClosedErrorCode.Idle);
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
    private protected void InitiateShutdown(string message, ConnectionClosedErrorCode errorCode)
    {
        lock (_mutex)
        {
            if (_disposeTask is not null || _shutdownTask is not null)
            {
                return;
            }

            ConnectionClosedException = new(errorCode, message);
            _shutdownTask = CreateShutdownTask(message);
        }
    }

    private protected abstract Task<IncomingResponse> InvokeAsyncCore(
        OutgoingRequest request,
        CancellationToken cancellationToken);

    private protected abstract Task ShutdownAsyncCore(string message, CancellationToken cancellationToken);

    private async Task CreateShutdownTask(string message, bool cancelDispatchesAndInvocations = false)
    {
        Debug.Assert(_connectTask is null || _connectTask.IsCompletedSuccessfully);

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
                    CancelDispatchesAndInvocations(
                        new ConnectionAbortedException(ConnectionAbortedErrorCode.Disposed, message));
                }
            }

            // Wait for shutdown to complete.
            await ShutdownAsyncCore(message, cts.Token).ConfigureAwait(false);

            _shutdownCompleteSource.SetResult(message);
        }
        catch (OperationCanceledException operationCanceledException)
        {
            Exception exception;

            if (_disposeTask is not null)
            {
                exception = new ConnectionAbortedException(ConnectionAbortedErrorCode.Disposed);
            }
            else if (_shutdownCts.IsCancellationRequested)
            {
                exception = new ConnectionAbortedException(ConnectionAbortedErrorCode.ShutdownCanceled);
            }
            else if (operationCanceledException.CancellationToken == cts.Token)
            {
                exception = new TimeoutException(
                    $"connection shutdown timed out after {_shutdownTimeout.TotalSeconds}s");
            }
            else
            {
                // By elimination
                exception = new ConnectionAbortedException(ConnectionAbortedErrorCode.ConnectCanceled);
            }

            _ = _shutdownCompleteSource.TrySetException(exception);
            throw exception;
        }
        catch (Exception ex)
        {
            Exception exception = new ConnectionAbortedException(
                _connectTask is null || _connectTask.IsCompletedSuccessfully ?
                    ConnectionAbortedErrorCode.ShutdownFailed :
                    ConnectionAbortedErrorCode.ConnectFailed,
                ex);
            _ = _shutdownCompleteSource.TrySetException(exception);
            throw exception;
        }
    }
}
