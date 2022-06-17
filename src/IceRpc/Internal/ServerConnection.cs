// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System.Diagnostics;

namespace IceRpc.Internal;

/// <summary>A connection created by a <see cref="Server"/>.</summary>
internal sealed class ServerConnection : IConnection
{
    /// <inheritdoc/>
    public bool IsResumable => false;

    /// <inheritdoc/>
    public NetworkConnectionInformation? NetworkConnectionInformation { get; private set; }

    /// <inheritdoc/>
    public Protocol Protocol => _protocolConnection.Protocol;

    private bool _isShutdown;

    private Task? _connectTask;

    private readonly TimeSpan _connectTimeout;

    // Prevent concurrent assignment of _connectTask and _isShutdown.
    private readonly object _mutex = new();

    private readonly IProtocolConnection _protocolConnection;

    private readonly TimeSpan _shutdownTimeout;

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
        using var connectCancellationSource = new CancellationTokenSource(_connectTimeout);

        lock (_mutex)
        {
            Debug.Assert(_connectTask == null);

            if (_isShutdown)
            {
                return Task.CompletedTask;
            }
            _connectTask = ConnectAsyncCore();
        }

        return _connectTask;

        async Task ConnectAsyncCore()
        {
            // Make sure we establish the connection asynchronously without holding any mutex lock from the caller.
            await Task.Yield();

            await _protocolConnection.ConnectAsync(
                isServer: true,
                this,
                connectCancellationSource.Token).ConfigureAwait(false);
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
            Debug.Assert(!_isShutdown);

            // TODO: Should we cancel the pending connect on ShutdownAsync or let it complete first?
            // if (_shutdownTask == null && _connectTask != null && !_connectTask.IsCompleted)
            // {
            //     _connectCancellationSource.Cancel();
            // }

            connectTask = _connectTask;
            _isShutdown = true;
        }

        if (connectTask != null)
        {
            try
            {
                // Wait for connection establishment to complete before calling ShutdownAsync.
                await connectTask.ConfigureAwait(false);
            }
            catch
            {
                // Ignore
            }
        }

        // If shutdown times out, abort the protocol connection.
        using var shutdownTimeoutCancellationSource = new CancellationTokenSource(_shutdownTimeout);
        using CancellationTokenRegistration _ = shutdownTimeoutCancellationSource.Token.Register(Abort);

        // Shutdown the protocol connection.
        await _protocolConnection.ShutdownAsync(message, cancel).ConfigureAwait(false);
    }
}
