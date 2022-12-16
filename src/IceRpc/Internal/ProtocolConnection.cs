// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System.Diagnostics;

namespace IceRpc.Internal;

/// <summary>The base implementation of <see cref="IProtocolConnection" />.</summary>
internal abstract class ProtocolConnection : IProtocolConnection
{
    public abstract ServerAddress ServerAddress { get; }

    public Task ShutdownComplete => _shutdownCompleteSource.Task;

    private protected bool IsServer { get; }

    // Derived classes need to be able to set the "no connection exception" with their mutex locked. We use an atomic
    // CompareExchange to avoid locking _mutex and to ensure we only set a single exception, the first one.
    private protected IceRpcException? ConnectionClosedException
    {
        get => Volatile.Read(ref _connectionClosedException);
        set
        {
            Debug.Assert(value is not null && value.IceRpcError == IceRpcError.ConnectionClosed);
            Interlocked.CompareExchange(ref _connectionClosedException, value, null);
        }
    }

    private IceRpcException? _connectionClosedException;
    private readonly CancellationTokenSource _connectCts = new();
    private Task<TransportConnectionInformation>? _connectTask;
    private readonly TimeSpan _connectTimeout;
    private Task? _disposeTask;
    private readonly TimeSpan _idleTimeout;
    private readonly Timer _idleTimeoutTimer;
    private readonly object _mutex = new();

    private readonly TaskCompletionSource _shutdownCompleteSource =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly CancellationTokenSource _shutdownCts = new();
    private Task? _shutdownTask;
    private readonly TimeSpan _shutdownTimeout;

    public Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken)
    {
        lock (_mutex)
        {
            if (_connectTask is not null)
            {
                throw new InvalidOperationException("The connect operation can be called only once.");
            }
            else if (_disposeTask is not null)
            {
                throw new ObjectDisposedException($"{typeof(ProtocolConnection)}");
            }

            // The connection can be closed only if disposed (and we check _disposedTask above).
            Debug.Assert(ConnectionClosedException is null);

            _connectTask = PerformConnectAsync();
        }
        return _connectTask;

        async Task<TransportConnectionInformation> PerformConnectAsync()
        {
            // Make sure we execute the function without holding the connection mutex lock.
            await Task.Yield();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _connectCts.Token);
            cts.CancelAfter(_connectTimeout);

            try
            {
                try
                {
                    TransportConnectionInformation information = await ConnectAsyncCore(
                        cts.Token).ConfigureAwait(false);
                    EnableIdleCheck();
                    return information;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    ConnectionClosedException = new(
                        IceRpcError.ConnectionClosed,
                        "The connection establishment was canceled.");

                    throw new OperationCanceledException(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    lock (_mutex)
                    {
                        if (_connectCts.IsCancellationRequested)
                        {
                            ConnectionClosedException = new(
                                IceRpcError.ConnectionClosed,
                                "The connection establishment was aborted.");

                            throw new IceRpcException(IceRpcError.OperationAborted);
                        }
                        else
                        {
                            ConnectionClosedException = new(
                                IceRpcError.ConnectionClosed,
                                "The connection establishment timeout out.");
                            throw new TimeoutException(
                                $"The connection establishment timed out after {_connectTimeout.TotalSeconds}s.");
                        }
                    }
                }
                catch (IceRpcException exception) when (exception.IceRpcError == IceRpcError.ConnectionRefused)
                {
                    ConnectionClosedException = new(
                        IceRpcError.ConnectionClosed,
                        "The connection was refused.",
                        exception);
                    throw;
                }
                catch (IceRpcException exception)
                {
                    ConnectionClosedException = new(
                        IceRpcError.ConnectionClosed,
                        "The connection establishment failed.",
                        exception);
                    throw;
                }
                catch (Exception exception)
                {
                    ConnectionClosedException = new(
                        IceRpcError.ConnectionClosed,
                        "The connection establishment failed.",
                        exception);
                    throw new IceRpcException(IceRpcError.IceRpcError, exception);
                }
            }
            catch
            {
                Debug.Assert(ConnectionClosedException is not null);
                _shutdownCompleteSource.TrySetException(ConnectionClosedException);
                throw;
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
            ConnectionClosedException = new(IceRpcError.ConnectionClosed, "The connection was disposed.");

            // Make sure we execute the code below without holding the mutex lock.
            await Task.Yield();

            // We don't lock _mutex since once _disposeTask is not null, _connectTask, _shutdownTask etc are read-only.

            if (_connectTask is null)
            {
                _ = _shutdownCompleteSource.TrySetResult(); // disposing non-connected connection
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
                        _shutdownTask = CreateShutdownTask(cancelDispatchesAndInvocations: true);
                    }
                    else if (!_shutdownTask.IsCanceled && !_shutdownTask.IsFaulted)
                    {
                        // Speed-up shutdown only if shutdown didn't fail.
                        CancelDispatchesAndInvocations();
                    }

                    await _shutdownTask.ConfigureAwait(false);
                }
                catch
                {
                    // The connection establishment or shutdown failed.
                }
            }

            await DisposeAsyncCore().ConfigureAwait(false);

            // Clean up disposable resources.
            await _idleTimeoutTimer.DisposeAsync().ConfigureAwait(false);
            _connectCts.Dispose();
            _shutdownCts.Dispose();
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
                Debug.Assert(ConnectionClosedException is not null);
                throw new ObjectDisposedException($"{typeof(ProtocolConnection)}", ConnectionClosedException);
            }
            else if (ConnectionClosedException is not null)
            {
                throw ConnectionClosedException;
            }
            else if (_connectTask is null)
            {
                throw new InvalidOperationException("Cannot call InvokeAsync before calling ConnectAsync.");
            }
        }

        if (_connectTask.IsCompleted)
        {
            return InvokeAsyncCore(request, cancellationToken);
        }
        else if (IsServer)
        {
            return PerformInvokeAsync();
        }
        else
        {
            throw new InvalidOperationException("Cannot call InvokeAsync while connecting a client connection.");
        }

        async Task<IncomingResponse> PerformInvokeAsync()
        {
            // It's possible to dispatch a request and expose its connection (invoker) before ConnectAsync completes;
            // in this rare case, we wait for _connectTask to complete before calling InvokeAsyncCore.
            _ = await _connectTask.ConfigureAwait(false);
            return await InvokeAsyncCore(request, cancellationToken).ConfigureAwait(false);
        }
    }

    public Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        lock (_mutex)
        {
            if (_disposeTask is not null)
            {
                throw new ObjectDisposedException($"{typeof(ProtocolConnection)}");
            }
            else if (_connectTask is null)
            {
                throw new InvalidOperationException("Cannot shut down a protocol connection before connecting it.");
            }

            ConnectionClosedException ??= new(IceRpcError.ConnectionClosed, "The connection was shut down.");

            if (_shutdownTask is null && _shutdownCompleteSource.Task.IsFaulted)
            {
                // The connection was aborted by the peer or the transport, but not yet shut down.

                Exception? shutdownException = null;
                _shutdownCompleteSource.Task.Exception!.Handle(
                    exception =>
                    {
                        shutdownException = exception;
                        return true;
                    });

                Debug.Assert(shutdownException is not null);

                _shutdownTask = Task.FromException(shutdownException);
                throw shutdownException;
            }

            // If cancellation is requested, we cancel shutdown right away. This is useful to ensure that the connection
            // is always aborted by DisposeAsync when calling ShutdownAsync(new CancellationToken(true)).
            if (cancellationToken.IsCancellationRequested)
            {
                var exception = new IceRpcException(IceRpcError.OperationAborted);
                _shutdownTask ??= Task.FromException(exception);
                _ = _shutdownCompleteSource.TrySetException(exception);
                _connectCts.Cancel();
                _shutdownCts.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }
            else
            {
                _shutdownTask ??= CreateShutdownTask();
            }
        }

        return PerformWaitForShutdownAsync();

        async Task PerformWaitForShutdownAsync()
        {
            try
            {
                await _shutdownTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException exception)
            {
                // _shutdownTask does not throw or complete with OperationCanceledException
                Debug.Assert(exception.CancellationToken == cancellationToken);

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
        _connectTimeout = options.ConnectTimeout;
        _shutdownTimeout = options.ShutdownTimeout;
        _idleTimeout = options.IdleTimeout;
        _idleTimeoutTimer = new Timer(_ =>
            {
                if (CheckIfIdle())
                {
                    InitiateShutdown(
                        $"The connection was closed because it was idle for over {_idleTimeout.TotalSeconds}s.");
                }
            });
        IsServer = isServer;
    }

    private protected abstract void CancelDispatchesAndInvocations();

    /// <summary>Checks if the connection is idle. If it's idle, the connection implementation should stop accepting new
    /// invocations and dispatches and return <see langword="true" /> and <see langword="false" /> otherwise.</summary>
    private protected abstract bool CheckIfIdle();

    private protected abstract Task<TransportConnectionInformation> ConnectAsyncCore(
        CancellationToken cancellationToken);

    private protected void ConnectionClosed(IceRpcException? exception = null) =>
        _ = exception is null ?
            _shutdownCompleteSource.TrySetResult() :
            _shutdownCompleteSource.TrySetException(exception);

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

            ConnectionClosedException = new(IceRpcError.ConnectionClosed, message);
            _shutdownTask = CreateShutdownTask();
        }
    }

    private protected abstract Task<IncomingResponse> InvokeAsyncCore(
        OutgoingRequest request,
        CancellationToken cancellationToken);

    private protected abstract Task ShutdownAsyncCore(CancellationToken cancellationToken);

    private async Task CreateShutdownTask(bool cancelDispatchesAndInvocations = false)
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
                try
                {
                    _ = await _connectTask.WaitAsync(cts.Token).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    // ConnectAsync timed out
                    throw new IceRpcException(IceRpcError.OperationAborted);
                }

                if (cancelDispatchesAndInvocations)
                {
                    CancelDispatchesAndInvocations();
                }
            }

            // Wait for shutdown to complete.
            await ShutdownAsyncCore(cts.Token).ConfigureAwait(false);

            _shutdownCompleteSource.SetResult();
        }
        catch (OperationCanceledException operationCanceledException)
        {
            Exception exception;

            if (_shutdownCts.IsCancellationRequested || operationCanceledException.CancellationToken != cts.Token)
            {
                // ShutdownAsync or ConnectAsync was canceled.
                exception = new IceRpcException(IceRpcError.OperationAborted);
            }
            else
            {
                Debug.Assert(operationCanceledException.CancellationToken == cts.Token);
                exception = new TimeoutException(
                    $"The connection shutdown timed out after {_shutdownTimeout.TotalSeconds} s.");
            }

            _connectCts.Cancel();
            _ = _shutdownCompleteSource.TrySetException(exception);
            throw exception;
        }
        catch (IceRpcException exception)
        {
            _connectCts.Cancel();
            _ = _shutdownCompleteSource.TrySetException(exception);
            throw;
        }
        catch (Exception exception)
        {
            _connectCts.Cancel();
            var newException = new IceRpcException(IceRpcError.IceRpcError, exception);
            _ = _shutdownCompleteSource.TrySetException(newException);
            throw newException;
        }
    }
}
