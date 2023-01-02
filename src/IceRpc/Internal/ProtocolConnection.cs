// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System.Diagnostics;

namespace IceRpc.Internal;

/// <summary>The base implementation of <see cref="IProtocolConnection" />.</summary>
internal abstract class ProtocolConnection : IProtocolConnection
{
    public Task<Exception?> Closed => _closedTcs.Task;

    public abstract ServerAddress ServerAddress { get; }

    public Task ShutdownRequested => _shutdownRequestedTcs.Task;

    private protected bool IsServer { get; }

    // Derived classes need to be able to set this exception with their mutex locked. We use an atomic
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

    private readonly TaskCompletionSource<Exception?> _closedTcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private IceRpcException? _connectionClosedException;
    private Task<TransportConnectionInformation>? _connectTask;
    private Task? _disposeTask;
    private readonly TimeSpan _idleTimeout;
    private readonly Timer _idleTimeoutTimer;
    private bool _isShutdown;
    private readonly object _mutex = new();

    // The thread that completes this TCS can run the continuations.
    private readonly TaskCompletionSource _shutdownRequestedTcs = new();

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
            Debug.Assert(!_isShutdown);
            Debug.Assert(ConnectionClosedException is null);

            _connectTask = PerformConnectAsync();
        }
        return _connectTask;

        async Task<TransportConnectionInformation> PerformConnectAsync()
        {
            // Make sure we execute the function without holding the connection mutex lock.
            await Task.Yield();

            try
            {
                try
                {
                    TransportConnectionInformation information = await ConnectAsyncCore(cancellationToken)
                        .ConfigureAwait(false);
                    EnableIdleCheck();
                    return information;
                }
                catch (OperationCanceledException)
                {
                    ConnectionClosedException = new(
                        IceRpcError.ConnectionClosed,
                        "The connection establishment was canceled.");

                    throw;
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
                _closedTcs.TrySetResult(ConnectionClosedException); // TODO: this is not correct
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
                _ = _closedTcs.TrySetResult(null); // disposing non-connected connection
            }
            else
            {
                try
                {
                    _ = await _connectTask.ConfigureAwait(false);
                }
                catch
                {
                    // ignore any ConnectAsync exception
                }

                if (_isShutdown)
                {
                    CancelDispatchesAndInvocations(); // speed up shutdown
                    _ = await Closed.ConfigureAwait(false);
                }
                else
                {
                    _ = _closedTcs.TrySetResult(
                        new IceRpcException(IceRpcError.OperationAborted, "The connection was disposed."));
                }
            }

            await DisposeAsyncCore().ConfigureAwait(false);

            // Clean up disposable resources.
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
                Debug.Assert(ConnectionClosedException is not null);
                throw new ObjectDisposedException($"{typeof(ProtocolConnection)}", ConnectionClosedException);
            }
            if (ConnectionClosedException is not null)
            {
                throw ConnectionClosedException;
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

        return InvokeAsyncCore(request, cancellationToken);
    }

    public Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        lock (_mutex)
        {
            if (_disposeTask is not null)
            {
                throw new ObjectDisposedException($"{typeof(ProtocolConnection)}");
            }
            if (_isShutdown)
            {
                throw new InvalidOperationException("Cannot call shutdown more than once.");
            }
            if (_connectTask is null || !_connectTask.IsCompletedSuccessfully)
            {
                throw new InvalidOperationException("Cannot shut down a protocol connection before connecting it.");
            }

            _isShutdown = true;
            ConnectionClosedException ??= new(IceRpcError.ConnectionClosed, "The connection was shut down.");

            if (_closedTcs.Task.IsCompletedSuccessfully && _closedTcs.Task.Result is Exception abortException)
            {
                // The connection was aborted by the peer or the transport, but not yet shut down.
                throw ExceptionUtil.Throw(abortException);
            }
        }

        return PerformShutdownAsync();

        async Task PerformShutdownAsync()
        {
            try
            {
                // Wait for connect to complete first.
                if (_connectTask is not null)
                {
                    try
                    {
                        _ = await _connectTask.WaitAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException exception) when (
                        exception.CancellationToken != cancellationToken)
                    {
                        // ConnectAsync was canceled.
                        throw new IceRpcException(
                            IceRpcError.OperationAborted,
                            "The shutdown was aborted because the connection establishment was canceled.");
                    }
                }

                await ShutdownAsyncCore(cancellationToken).ConfigureAwait(false);
                _closedTcs.SetResult(null);
            }
            catch (OperationCanceledException)
            {
                Exception newException = new IceRpcException(
                    IceRpcError.OperationAborted,
                    "The shutdown was canceled.");

                _ = _closedTcs.TrySetResult(newException);
                throw;
            }
            catch (IceRpcException exception)
            {
                _ = _closedTcs.TrySetResult(exception);
                throw;
            }
            catch (Exception exception)
            {
                var newException = new IceRpcException(IceRpcError.IceRpcError, exception);
                _ = _closedTcs.TrySetResult(newException);
                throw newException;
            }
        }
    }

    internal ProtocolConnection(bool isServer, ConnectionOptions options)
    {
        _idleTimeout = options.IdleTimeout;
        _idleTimeoutTimer = new Timer(_ =>
            {
                if (CheckIfIdle())
                {
                    RequestShutdown(
                        $"The connection was shut down because it was idle for over {_idleTimeout.TotalSeconds} s.");
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
        _closedTcs.TrySetResult(exception);

    private protected void DisableIdleCheck() =>
        _idleTimeoutTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

    private protected abstract ValueTask DisposeAsyncCore();

    private protected void EnableIdleCheck() =>
        _idleTimeoutTimer.Change(_idleTimeout, Timeout.InfiniteTimeSpan);

    private protected abstract Task<IncomingResponse> InvokeAsyncCore(
        OutgoingRequest request,
        CancellationToken cancellationToken);

    private protected void RequestShutdown(string message)
    {
        ConnectionClosedException ??= new(IceRpcError.ConnectionClosed, message);
        _shutdownRequestedTcs.TrySetResult();
    }

    private protected abstract Task ShutdownAsyncCore(CancellationToken cancellationToken);
}
