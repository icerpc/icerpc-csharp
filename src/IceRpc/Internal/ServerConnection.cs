// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System.Diagnostics;

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

    private readonly CancellationTokenSource _connectCancellationSource = new();

    // The only reason we have a _connectTask is to wait for its completion during shutdown.
    private Task? _connectTask;

    private readonly TimeSpan _connectTimeout;

    private bool _isShutdown;

    // Prevent concurrent assignment of _connectTask and _isShutdown.
    private readonly object _mutex = new();

    private readonly IProtocolConnection _protocolConnection;

    private readonly TimeSpan _shutdownTimeout;

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await ShutdownAsync(new CancellationToken(canceled: true)).ConfigureAwait(false);
        _connectCancellationSource.Dispose();
    }

    /// <inheritdoc/>
    public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel) =>
        _protocolConnection.InvokeAsync(request, this, cancel);

    /// <inheritdoc/>
    public void OnClose(Action<Exception> callback) => _protocolConnection.OnClose(callback);

    /// <summary>Constructs a server connection from an accepted network connection.</summary>
    internal ServerConnection(IProtocolConnection protocolConnection, ConnectionOptions options)
    {
        _protocolConnection = protocolConnection;
        _connectTimeout = options.ConnectTimeout;
        _shutdownTimeout = options.CloseTimeout;
    }

    /// <summary>Aborts the connection.</summary>
    internal void Abort() => _protocolConnection.Abort(new ConnectionAbortedException());

    /// <summary>Establishes the connection.</summary>
    /// <returns>A task that indicates the completion of the connect operation.</returns>
    internal Task ConnectAsync()
    {
        Debug.Assert(_connectTask == null); // called at most once

        lock (_mutex)
        {
            if (_isShutdown)
            {
                return Task.CompletedTask;
            }
            _connectTask = PerformConnectAsync();
        }

        return _connectTask;

        async Task PerformConnectAsync()
        {
            // Make sure we establish the connection asynchronously without holding any mutex lock from the caller.
            await Task.Yield();

            _connectCancellationSource.CancelAfter(_connectTimeout);

            // Even though this is not atomic, nobody can get hold of this connection before the connection is
            // established
            NetworkConnectionInformation = await _protocolConnection.ConnectAsync(
                isServer: true,
                this,
                _connectCancellationSource.Token).ConfigureAwait(false);
        }
    }

    /// <summary>Gracefully shuts down of the connection. If ShutdownAsync is canceled, dispatch and invocations are
    /// canceled. Shutdown cancellation can lead to a speedier shutdown if dispatch are cancelable.</summary>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    internal Task ShutdownAsync(CancellationToken cancel = default) => ShutdownAsync("connection shutdown", cancel);

    /// <summary>Gracefully shuts down of the connection. If ShutdownAsync is canceled, dispatch and invocations are
    /// canceled. Shutdown cancellation can lead to a speedier shutdown if dispatch are cancelable.</summary>
    /// <param name="message">The message transmitted to the peer (when using the IceRPC protocol).</param>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    internal async Task ShutdownAsync(string message, CancellationToken cancel = default)
    {
        Task? connectTask = null;
        lock (_mutex)
        {
            _isShutdown = true;
            connectTask = _connectTask;
        }

        if (connectTask == null)
        {
            // ConnectAsync wasn't called, just release resources associated with the protocol connection.
            // TODO: Refactor depending on what we decide for the protocol connection resource cleanup (#1397,
            // #1372, #1404, #1400).
            _protocolConnection.Abort(new ConnectionClosedException());
        }
        else
        {
            try
            {
                // Wait for connection establishment to complete before calling ShutdownAsync.
                await connectTask.ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                // Protocol connection resource cleanup. This is for now performed by Abort (which should have
                // been named CloseAsync like ConnectionCore.CloseAsync).
                // TODO: Refactor depending on what we decide for the protocol connection resource cleanup (#1397,
                // #1372, #1404, #1400).
                _protocolConnection.Abort(exception);
                return;
            }

            // If shutdown times out, abort the protocol connection.
            using var shutdownTimeoutCancellationSource = new CancellationTokenSource(_shutdownTimeout);
            using CancellationTokenRegistration _ = shutdownTimeoutCancellationSource.Token.Register(Abort);

            // Shutdown the protocol connection.
            try
            {
                await _protocolConnection.ShutdownAsync(message, cancel).ConfigureAwait(false);
            }
            catch
            {
                // Ignore, this can occur if the protocol conneciton is aborted.
            }
        }
    }
}
