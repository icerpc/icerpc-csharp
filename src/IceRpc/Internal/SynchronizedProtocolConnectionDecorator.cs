// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System.Diagnostics;

namespace IceRpc.Internal;

/// <summary>A class to provide ordering and synchronization guarantees to protocol implementations. It ensures that
/// ConnectAsync, ShutdownAsync and DisposedAsync are only called once. ShutdownAsync waits for the ConnectAsync to
/// complete. DisposeAsync cancels the pending ConnectAsync and ShutdownAsync waits for their completion before calling
/// DisposedAsync on the decoratee.</summary>
internal class SynchronizedProtocolConnectionDecorator : IProtocolConnection
{
    public Protocol Protocol => _decoratee.Protocol;

    private Task<NetworkConnectionInformation>? _connectTask;
    private readonly TimeSpan _connectTimeout;
    private readonly IProtocolConnection _decoratee;
    private readonly CancellationTokenSource _disposeCancellationSource = new();
    private Task? _disposeTask;
    private readonly object _mutex = new();
    private Action<Exception>? _onAbort;
    private Action<string>? _onShutdown;
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
            else if (_connectTask is null)
            {
                Debug.Assert(_shutdownTask == null); // ShutdownAsync returns if the connection is not connected.

                // Execute the connect state function.
                _connectTask = ExecuteStateFunc(
                    cancel => _decoratee.ConnectAsync(connection, cancel),
                    "connect",
                    _connectTimeout,
                    cancel);
            }
        }
        return _connectTask;
    }

    public Task<IncomingResponse> InvokeAsync(
        OutgoingRequest request,
        IConnection connection,
        CancellationToken cancel)
    {
        if (_shutdownTask is not null)
        {
            throw new ConnectionClosedException("connection is shutdown");
        }
        else if (_connectTask is not null && _connectTask.IsCompletedSuccessfully)
        {
            return _decoratee.InvokeAsync(request, connection, cancel);
        }
        else
        {
            return PerformInvokeAsync();
        }

        async Task<IncomingResponse> PerformInvokeAsync()
        {
            await ConnectAsync(connection, CancellationToken.None).WaitAsync(cancel).ConfigureAwait(false);
            return await _decoratee.InvokeAsync(request, connection, cancel).ConfigureAwait(false);
        }
    }

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
            else if (_connectTask.IsFaulted || _connectTask.IsCanceled)
            {
                throw new ConnectionAbortedException("connection establishment failed");
            }
            else if (_shutdownTask is null)
            {
                _shutdownTask = ExecuteStateFunc(
                    async cancel =>
                    {
                        // Wait for connection establishment to complete.
                        await _connectTask.ConfigureAwait(false);

                        // Wait for shutdown to complete.
                        await _decoratee.ShutdownAsync(message, cancel).ConfigureAwait(false);

                        return true; // ExecuteStateFunc requires a function with a return value.
                    },
                    "shutdown",
                    _shutdownTimeout,
                    cancel);
            }
        }
        return _shutdownTask;
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
            // Make sure we execute the function without holding the state machine mutex lock.
            await Task.Yield();

            // Cancel pending ConnectAsync or ShutdownAsync.
            _disposeCancellationSource.Cancel();

            // Wait for ConnectAsync to complete.
            if (_connectTask is not null)
            {
                try
                {
                    await _connectTask.ConfigureAwait(false);
                }
                catch
                {
                    // Ignore
                }
            }

            // Wait for ShutdownAsync to complete.
            if (_shutdownTask is not null)
            {
                try
                {
                    await _shutdownTask.ConfigureAwait(false);
                }
                catch
                {
                    // Ignore
                }
            }

            // Execute the dispose task.
            await _decoratee.DisposeAsync().ConfigureAwait(false);

            // Cleans up disposable resources.
            _disposeCancellationSource.Dispose();
        }
    }

    internal SynchronizedProtocolConnectionDecorator(
        IProtocolConnection decoratee,
        TimeSpan connectTimeout,
        TimeSpan shutdownTimeout)
    {
        _decoratee = decoratee;
        _connectTimeout = connectTimeout;
        _shutdownTimeout = shutdownTimeout;
        _decoratee.OnAbort(exception =>
        {
            Action<Exception>? onAbort = null;
            lock (_mutex)
            {
                if (_disposeTask is null)
                {
                    onAbort = _onAbort;
                }
            }
            onAbort?.Invoke(exception);
        });
        _decoratee.OnShutdown(message =>
        {
            Action<string>? onShutdown = null;
            lock (_mutex)
            {
                if (_shutdownTask is null)
                {
                    onShutdown = _onShutdown;
                }
            }
            onShutdown?.Invoke(message);
        });
    }

    private async Task<T> ExecuteStateFunc<T>(
        Func<CancellationToken, Task<T>> stateFunc,
        string state,
        TimeSpan timeout,
        CancellationToken cancel)
    {
        // Cancel the state function either if DisposeAsync is called, the given cancellation token is canceled or the
        // timeout is triggered.
        using var linkedCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(
            _disposeCancellationSource.Token,
            cancel);
        linkedCancellationSource.CancelAfter(timeout);

        try
        {
            return await stateFunc(linkedCancellationSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_disposeCancellationSource.IsCancellationRequested)
        {
            // DisposeAsync has been called.
            throw new ConnectionAbortedException($"connection {state} aborted because the connection was disposed");
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            // Triggered by the CancelAfter above.
            throw new TimeoutException($"connection {state} timed out after {timeout.TotalSeconds}s");
        }
    }
}
