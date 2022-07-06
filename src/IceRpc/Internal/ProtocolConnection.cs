// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System.Diagnostics;

namespace IceRpc.Internal;

/// <summary>The protocol connection abstract base class provides the idle timeout implementation and also ensures that
/// the protocol implementation of connection establishment, shutdown and disposal are only called once and in the
/// correct order.</summary>
internal abstract class ProtocolConnection : IProtocolConnection
{
    public abstract Protocol Protocol { get; }

    private readonly CancellationTokenSource _connectCancelSource = new();
    private Task<NetworkConnectionInformation>? _connectTask;
    private readonly TimeSpan _connectTimeout;
    private Task? _disposeTask;
    private readonly TimeSpan _idleTimeout;
    private readonly Timer _idleTimeoutTimer;
    private readonly object _mutex = new();
    private Action<Exception>? _onAbort;
    private Action<string>? _onShutdown;
    private readonly CancellationTokenSource _shutdownCancelSource = new();
    private Task? _shutdownTask;
    private readonly TimeSpan _shutdownTimeout;

    public Task<NetworkConnectionInformation> ConnectAsync(IConnection connection, CancellationToken cancel)
    {
        lock (_mutex)
        {
            if (_disposeTask is not null)
            {
                throw new ObjectDisposedException($"{typeof(IProtocolConnection)}");
            }
            _connectTask ??= ConnectAsyncCore();
        }

        return WaitForConnectAsync();

        async Task<NetworkConnectionInformation> WaitForConnectAsync()
        {
            try
            {
                return await _connectTask.WaitAsync(cancel).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (ex.CancellationToken == cancel)
            {
                // Cancel pending connect if any of the ConnectAsync calls are canceled.
                try
                {
                    _connectCancelSource.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
                throw;
            }
        }

        async Task<NetworkConnectionInformation> ConnectAsyncCore()
        {
            // Make sure we execute the function without holding the connection mutex lock.
            await Task.Yield();

            using var cancelSource = CancellationTokenSource.CreateLinkedTokenSource(_connectCancelSource.Token);
            cancelSource.CancelAfter(_connectTimeout);

            try
            {
                NetworkConnectionInformation information = await PerformConnectAsync(
                    connection,
                    cancelSource.Token).ConfigureAwait(false);
                EnableIdleCheck();
                return information;
            }
            catch (OperationCanceledException) when (_connectCancelSource.IsCancellationRequested)
            {
                throw new ConnectionAbortedException(_disposeTask is null ?
                    "connection establishment canceled" :
                    "connection disposed");
            }
            catch (OperationCanceledException)
            {
                Debug.Assert(cancelSource.IsCancellationRequested);
                throw new TimeoutException($"connection establishment timed out after {_connectTimeout.TotalSeconds}s");
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
            _disposeTask ??= DisposeAsyncCore();
        }
        return new(_disposeTask);

        async Task DisposeAsyncCore()
        {
            // Make sure we execute the function without holding the mutex lock.
            await Task.Yield();

            // Cancel connect or shutdown if pending.
            if (_connectTask is not null && !_connectTask.IsCompleted)
            {
                _connectCancelSource.Cancel();
            }
            if (_shutdownTask is not null && !_shutdownTask.IsCompleted)
            {
                _shutdownCancelSource.Cancel();
            }

            // If connection establishment succeeded but shutdown is not initiated, let's initiate a speedy shutdown.
            if (_connectTask is not null && _connectTask.IsCompletedSuccessfully && _shutdownTask is null)
            {
                _shutdownTask = ShutdownAsyncCore("connection disposed", speedy: true);
            }

            // Wait for shutdown or connection establishment to complete.
            try
            {
                if (_shutdownTask is not null)
                {
                    await _shutdownTask.ConfigureAwait(false);
                }
                else if (_connectTask is not null)
                {
                    await _connectTask.ConfigureAwait(false);
                }
            }
            catch
            {
                // Ignore
            }

            await PerformDisposeAsync().ConfigureAwait(false);

            // Clean up disposable resources.
            await _idleTimeoutTimer.DisposeAsync().ConfigureAwait(false);
            _connectCancelSource.Dispose();
            _shutdownCancelSource.Dispose();
        }
    }

    public Task<IncomingResponse> InvokeAsync(
        OutgoingRequest request,
        IConnection connection,
        CancellationToken cancel)
    {
        if (_shutdownTask is not null)
        {
            throw new ConnectionClosedException(
                _shutdownTask.IsCompleted ? "connection is shutdown" : "connection is shutting down");
        }
        else if (_connectTask is not null && _connectTask.IsCompletedSuccessfully)
        {
            return PerformInvokeAsync(request, connection, cancel);
        }
        else
        {
            return ConnectAndPerformInvokeAsync();
        }

        async Task<IncomingResponse> ConnectAndPerformInvokeAsync()
        {
            // Perform the connection establishment without any cancellation token. It will eventually timeout if the
            // connect timeout is reached. However, we don't wait for it to complete with the given cancellation token
            // is canceled.
            await ConnectAsync(connection, CancellationToken.None).WaitAsync(cancel).ConfigureAwait(false);

            return await PerformInvokeAsync(request, connection, cancel).ConfigureAwait(false);
        }
    }

    /// <summary>Registers a callback that will be called when the connection is aborted by the peer.</summary>
    public void OnAbort(Action<Exception> callback)
    {
        bool executeCallback = false;

        lock (_mutex)
        {
            if (_disposeTask is null)
            {
                _onAbort += callback;
            }
            else
            {
                executeCallback = true;
            }
        }

        if (executeCallback)
        {
            callback(new ConnectionAbortedException("connection disposed"));
        }
    }

    /// <summary>Registers a callback that will be called when the connection is shutdown by the idle timeout or by
    /// by the peer.</summary>
    public void OnShutdown(Action<string> callback)
    {
        bool executeCallback = false;

        lock (_mutex)
        {
            if (_shutdownTask is null)
            {
                _onShutdown += callback;
            }
            else
            {
                executeCallback = true;
            }
        }

        if (executeCallback)
        {
            callback("");
        }
    }

    public Task ShutdownAsync(string message, CancellationToken cancel = default)
    {
        lock (_mutex)
        {
            if (_disposeTask is not null)
            {
                throw new ObjectDisposedException($"{typeof(IProtocolConnection)}");
            }
            else if (_connectTask is null)
            {
                return Task.CompletedTask;
            }
            else if (_connectTask.IsCanceled || _connectTask.IsFaulted)
            {
                throw new ConnectionAbortedException("connection establishment failed");
            }

            // If cancellation is requested, we cancel shutdown right away. This is useful to ensure that the connection
            // is always aborted by DisposeAsync when calling ShutdownAsync(new CancellationToken(true)).
            if (cancel.IsCancellationRequested)
            {
                _shutdownCancelSource.Cancel();
            }

            _shutdownTask ??= ShutdownAsyncCore(message, speedy: false);
        }

        return WaitForShutdownAsync();

        async Task WaitForShutdownAsync()
        {
            try
            {
                await _shutdownTask.WaitAsync(cancel).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (ex.CancellationToken == cancel)
            {
                try
                {
                    _shutdownCancelSource.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
                throw;
            }
        }
    }

    internal ProtocolConnection(ConnectionOptions options)
    {
        _connectTimeout = options.ConnectTimeout;
        _shutdownTimeout = options.ShutdownTimeout;
        _idleTimeout = options.IdleTimeout;
        _idleTimeoutTimer = new Timer(_ =>
            {
                if (CheckIfIdle())
                {
                    InitiateShutdown("idle connection");
                }
            });
    }

    /// <summary>Checks if the connection is idle. If it's idle, the connection implementation should stop accepting new
    /// invocations and dispatches and return <c>true</c> and <c>false</c> otherwise.</summary>
    private protected abstract bool CheckIfIdle();

    private protected void DisableIdleCheck() =>
        _idleTimeoutTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

    private protected void EnableIdleCheck() =>
        _idleTimeoutTimer.Change(_idleTimeout, Timeout.InfiniteTimeSpan);

    /// <summary>Initiate shutdown if it's not already initiated. The <see cref="OnShutdown"/> callback is notified
    /// when shutdown is initiated through this method.</summary>
    private protected void InitiateShutdown(string message)
    {
        Action<string>? onShutdown = null;
        lock (_mutex)
        {
            if (_shutdownTask is null)
            {
                onShutdown = _onShutdown;
                _onShutdown = null;
                _shutdownTask = ShutdownAsyncCore(message, speedy: false);
            }
        }
        onShutdown?.Invoke(message);
    }

    private protected void InvokeOnAbort(Exception exception)
    {
        Action<Exception>? onAbort;
        lock (_mutex)
        {
            onAbort = _onAbort;
            _onAbort = null;
        }
        onAbort?.Invoke(exception);
    }

    private protected void InvokeOnShutdown(string message)
    {
        Action<string>? onShutdown;
        lock (_mutex)
        {
            onShutdown = _onShutdown;
            _onShutdown = null;
        }
        onShutdown?.Invoke(message);
    }

    private protected abstract Task<NetworkConnectionInformation> PerformConnectAsync(
        IConnection connection,
        CancellationToken cancel);

    private protected abstract ValueTask PerformDisposeAsync();

    private protected abstract Task<IncomingResponse> PerformInvokeAsync(
        OutgoingRequest request,
        IConnection connection,
        CancellationToken cancel);

    private protected abstract Task PerformShutdownAsync(string message, bool speedy, CancellationToken cancel);

    private async Task ShutdownAsyncCore(string message, bool speedy)
    {
        Debug.Assert(_connectTask is not null);

        // Make sure we execute the function without holding the connection mutex lock.
        await Task.Yield();

        using var cancelSource = CancellationTokenSource.CreateLinkedTokenSource(_shutdownCancelSource.Token);
        cancelSource.CancelAfter(_shutdownTimeout);

        try
        {
            // Wait for connect to complete first.
            await _connectTask.WaitAsync(cancelSource.Token).ConfigureAwait(false);

            // Wait for shutdown to complete.
            await PerformShutdownAsync(message, speedy, cancelSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_shutdownCancelSource.IsCancellationRequested)
        {
            throw new ConnectionAbortedException(
                _disposeTask is null ? "connection shutdown canceled" : "connection disposed");
        }
        catch (OperationCanceledException)
        {
            Debug.Assert(cancelSource.IsCancellationRequested);

            // Triggered by the CancelAfter above.
            throw new TimeoutException($"connection shutdown timed out after {_shutdownTimeout.TotalSeconds}s");
        }
    }
}
