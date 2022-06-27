// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc.Internal;

/// <summary>A connection created by a <see cref="Server"/>.</summary>
internal sealed class ServerConnection : IConnection, IAsyncDisposable
{
    /// <inheritdoc/>
    public bool IsResumable => false;

    /// <inheritdoc/>
    public NetworkConnectionInformation? NetworkConnectionInformation { get; private set; }

    /// <inheritdoc/>
    public Protocol Protocol => _protocolConnection.Protocol;

    // The only reason we have a _connectTask is to wait for its completion during shutdown.
    private Task? _connectTask;

    private readonly TimeSpan _connectTimeout;

    private Task? _disposeTask;

    // Prevent concurrent assignment of _connectTask, _disposeTask and _isShutdown.
    private readonly object _mutex = new();

    private readonly IProtocolConnection _protocolConnection;

    private readonly CancellationTokenSource _protocolConnectionCancellationSource = new();

    private readonly TimeSpan _shutdownTimeout;

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        // DisposeAsync can be called concurrently. For example, Server can dispose a connection because the client is
        // shutting down and at the same time or shortly after dispose the same connection because of its own disposal.
        // We want to second disposal to "hang" if there is (for example) a bug in the dispatch code that causes the
        // DisposeAsync to hang.

        lock (_mutex)
        {
            _disposeTask ??= PerformDisposeAsync();
        }

        await _disposeTask.ConfigureAwait(false);

        async Task PerformDisposeAsync()
        {
            await Task.Yield();

            // TODO: temporary way to cancel dispatches and abort invocations.
            _protocolConnectionCancellationSource.Cancel();

            try
            {
                await ShutdownAsync("server connection disposed", CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
            }

            // TODO: await _protocolConnection.DisposeAsync();
            _protocolConnectionCancellationSource.Dispose();
        }
    }

    /// <inheritdoc/>
    public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel) =>
        _protocolConnection.InvokeAsync(request, this, cancel);

    /// <inheritdoc/>
    public void OnAbort(Action<Exception> callback) => _protocolConnection.OnAbort(callback);

    /// <inheritdoc/>
    public void OnShutdown(Action<string> callback) => _protocolConnection.OnShutdown(callback);

    /// <summary>Constructs a server connection from an accepted network connection.</summary>
    internal ServerConnection(IProtocolConnection protocolConnection, ConnectionOptions options)
    {
        _protocolConnection = protocolConnection;
        _connectTimeout = options.ConnectTimeout;
        _shutdownTimeout = options.ShutdownTimeout;
    }

    /// <summary>Aborts the connection.</summary>
    internal void Abort() => _protocolConnection.Abort(new ConnectionAbortedException());

    /// <summary>Establishes the connection.</summary>
    /// <returns>A task that indicates the completion of the connect operation.</returns>
    internal Task ConnectAsync()
    {
        lock (_mutex)
        {
            ThrowIfDisposed();
            _connectTask ??= PerformConnectAsync();
        }

        return _connectTask;

        async Task PerformConnectAsync()
        {
            // Make sure we establish the connection asynchronously without holding any mutex lock from the caller.
            await Task.Yield();

            using var tokenSource = new CancellationTokenSource(_connectTimeout);

            try
            {
                // Even though this assignment is not atomic, it's ok because nobody can get hold of this connection
                // before the connection is established.
                NetworkConnectionInformation = await _protocolConnection.ConnectAsync(
                    isServer: true,
                    this,
                    tokenSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                var timeoutException = new TimeoutException(
                    $"connection establishment timed out after {_connectTimeout.TotalSeconds}s");
                _protocolConnection.Abort(timeoutException);
                throw timeoutException;
            }
            catch (Exception exception)
            {
                _protocolConnection.Abort(exception);
                throw;
            }
        }
    }

    /// <summary>Gracefully shuts down the connection.</summary>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    internal Task ShutdownAsync(CancellationToken cancel = default) =>
        ShutdownAsync("server connection shutdown", cancel);

    /// <summary>Gracefully shuts down the connection.</summary>
    /// <param name="message">The message transmitted to the client when using the IceRPC protocol.</param>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    internal async Task ShutdownAsync(string message, CancellationToken cancel = default)
    {
        lock (_mutex)
        {
            ThrowIfDisposed();
            _connectTask ??= Task.CompletedTask;
        }

        // TODO: deal with concurrent calls to ShutdownAsync? Seems easier to move this logic to
        // _protocolConnection.ShutdownAsync

        // Need second token to figure out if the call exceeded _shutdownTimeout or was canceled for another reason:
        using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        linkedTokenSource.CancelAfter(_shutdownTimeout);

        try
        {
            await ConnectAsync().WaitAsync(linkedTokenSource.Token).ConfigureAwait(false);

            await _protocolConnection.ShutdownAsync(
                message,
                _protocolConnectionCancellationSource.Token).WaitAsync(linkedTokenSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            // Triggered by the CancelAfter above
            var timeoutException = new TimeoutException(
                $"connection shutdown timed out after {_shutdownTimeout.TotalSeconds}s");
            _protocolConnection.Abort(timeoutException);
            throw timeoutException;
        }
        catch (Exception exception)
        {
            _protocolConnection.Abort(exception);
            throw;
        }
    }

    private void ThrowIfDisposed()
    {
        // Must be called with _mutex locked.
        if (_disposeTask is Task disposeTask && disposeTask.IsCompleted)
        {
            throw new ObjectDisposedException($"{typeof(ServerConnection)}");
        }
    }
}
