// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System.Diagnostics;

namespace IceRpc.Internal;

/// <summary>The base implementation of <see cref="IProtocolConnection"/>.</summary>
internal abstract class ProtocolConnection : IProtocolConnection
{
    public abstract ServerAddress ServerAddress { get; }

    private CancellationTokenSource? _connectCts;
    private Task<TransportConnectionInformation>? _connectTask;
    private readonly TimeSpan _connectTimeout;
    private Task? _disposeTask;
    private readonly TimeSpan _idleTimeout;
    private readonly Timer _idleTimeoutTimer;
    private readonly object _mutex = new();
    private Action<Exception>? _onAbort;
    private Exception? _onAbortException;
    private Action<string>? _onShutdown;
    private string? _onShutdownMessage;
    private readonly CancellationTokenSource _shutdownCts = new();
    private Task? _shutdownTask;
    private readonly TimeSpan _shutdownTimeout;

    public Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancel)
    {
        lock (_mutex)
        {
            if (_disposeTask is not null)
            {
                throw new ObjectDisposedException($"{typeof(ProtocolConnection)}");
            }
            else if (_shutdownTask is not null)
            {
                throw new ConnectionClosedException(
                    _shutdownTask.IsCompleted ? "connection is shutdown" : "connection is shutting down");
            }
            else if (_connectTask is not null)
            {
                throw new InvalidOperationException("unexpected second call to ConnectAsync");
            }
            else
            {
                _connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancel);
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
                cancel.ThrowIfCancellationRequested();

                lock (_mutex)
                {
                    if (_disposeTask is not null)
                    {
                        throw new ConnectionAbortedException("connection disposed");
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
            // Make sure we execute the function without holding the mutex lock.
            await Task.Yield();

            if (_connectTask is not null)
            {
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
                        _shutdownTask = CreateShutdownTask("connection dispose", cancelDispatchesAndInvocations: true);
                    }
                    else if (!_shutdownTask.IsCanceled && !_shutdownTask.IsFaulted)
                    {
                        // Speed-up shutdown only if shutdown didn't fail.
                        CancelDispatchesAndInvocations(new ConnectionAbortedException("connection disposed"));
                    }

                    try
                    {
                        await _shutdownTask.ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                }
            }

            await DisposeAsyncCore().ConfigureAwait(false);

            // Clean up disposable resources.
            await _idleTimeoutTimer.DisposeAsync().ConfigureAwait(false);
            _connectCts?.Dispose();
            _shutdownCts.Dispose();
        }
    }

    public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel = default)
    {
        if (request.Protocol != ServerAddress.Protocol)
        {
            throw new InvalidOperationException(
                $"cannot send {request.Protocol} request on {ServerAddress.Protocol} connection");
        }

        if (_shutdownTask is not null)
        {
            throw new ConnectionClosedException(
                _shutdownTask.IsCompleted ? "connection is shutdown" : "connection is shutting down");
        }
        else if (_connectTask is null || !_connectTask.IsCompletedSuccessfully)
        {
            throw new InvalidOperationException("cannot call InvokeAsync before calling ConnectAsync");
        }

        return InvokeAsyncCore(request, cancel);
    }

    public void OnAbort(Action<Exception> callback)
    {
        bool executeCallback = false;

        lock (_mutex)
        {
            if (_onAbortException is null)
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
            callback(_onAbortException!);
        }
    }

    public void OnShutdown(Action<string> callback)
    {
        bool executeCallback = false;

        lock (_mutex)
        {
            if (_onShutdownMessage is null)
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
            callback(_onShutdownMessage!);
        }
    }

    public Task ShutdownAsync(string message, CancellationToken cancel = default)
    {
        lock (_mutex)
        {
            if (_disposeTask is not null)
            {
                throw new ObjectDisposedException($"{typeof(ProtocolConnection)}");
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
                _shutdownTask ??= Task.FromException(new ConnectionAbortedException("connection shutdown canceled"));
                _shutdownCts.Cancel();
                cancel.ThrowIfCancellationRequested();
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
                await _shutdownTask.WaitAsync(cancel).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (ex.CancellationToken == cancel)
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

    private protected abstract void CancelDispatchesAndInvocations(Exception exception);

    /// <summary>Checks if the connection is idle. If it's idle, the connection implementation should stop accepting new
    /// invocations and dispatches and return <c>true</c> and <c>false</c> otherwise.</summary>
    private protected abstract bool CheckIfIdle();

    private protected abstract Task<TransportConnectionInformation> ConnectAsyncCore(CancellationToken cancel);

    private protected void DisableIdleCheck() =>
        _idleTimeoutTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

    private protected abstract ValueTask DisposeAsyncCore();

    private protected void EnableIdleCheck() =>
        _idleTimeoutTimer.Change(_idleTimeout, Timeout.InfiniteTimeSpan);

    /// <summary>Initiate shutdown if it's not already initiated. The <see cref="OnShutdown"/> callback is notified
    /// when shutdown is initiated through this method.</summary>
    private protected void InitiateShutdown(string message)
    {
        lock (_mutex)
        {
            if (_disposeTask is not null || _shutdownTask is not null)
            {
                return;
            }
            Debug.Assert(_connectTask is not null);

            _shutdownTask = CreateShutdownTask(message);
        }
        InvokeOnShutdown(message);
    }

    private protected abstract Task<IncomingResponse> InvokeAsyncCore(
        OutgoingRequest request,
        CancellationToken cancel);

    private protected void InvokeOnAbort(Exception exception)
    {
        lock (_mutex)
        {
            if (_onAbortException is not null)
            {
                return;
            }
            _onAbortException = exception;
        }
        _onAbort?.Invoke(exception);
    }

    private protected void InvokeOnShutdown(string message)
    {
        lock (_mutex)
        {
            if (_onShutdownMessage is not null)
            {
                return;
            }
            _onShutdownMessage = message;
        }
        _onShutdown?.Invoke(message);
    }

    private protected abstract Task ShutdownAsyncCore(string message, CancellationToken cancel);

    private async Task CreateShutdownTask(string message, bool cancelDispatchesAndInvocations = false)
    {
        Debug.Assert(_connectTask is not null);

        // Make sure we execute the function without holding the connection mutex lock.
        await Task.Yield();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_shutdownCts.Token);
        cts.CancelAfter(_shutdownTimeout);

        try
        {
            cts.Token.ThrowIfCancellationRequested();

            // Wait for connect to complete first.
            _ = await _connectTask.WaitAsync(cts.Token).ConfigureAwait(false);

            if (cancelDispatchesAndInvocations)
            {
                CancelDispatchesAndInvocations(new ConnectionAbortedException(message));
            }

            // Wait for shutdown to complete.
            await ShutdownAsyncCore(message, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_shutdownCts.IsCancellationRequested)
        {
            throw new ConnectionAbortedException(
                _disposeTask is null ? "connection shutdown canceled" : "connection disposed");
        }
        catch (OperationCanceledException)
        {
            Debug.Assert(cts.IsCancellationRequested);

            // Triggered by the CancelAfter above.
            throw new TimeoutException($"connection shutdown timed out after {_shutdownTimeout.TotalSeconds}s");
        }
    }
}
