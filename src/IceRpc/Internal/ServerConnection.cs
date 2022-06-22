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

    // The only reason we have a _connectTask is to wait for its completion during shutdown.
    private Task? _connectTask;

    private readonly TimeSpan _connectTimeout;

    private bool _isDisposed;

    private bool _isShutdown;

    // Prevent concurrent assignment of _connectTask and _isShutdown.
    private readonly object _mutex = new();

    private readonly IProtocolConnection _protocolConnection;

    private readonly CancellationTokenSource _protocolConnectionCancellationSource = new();

    private readonly TimeSpan _shutdownTimeout;

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        lock (_mutex)
        {
            if (_isDisposed)
            {
                return;
            }
            _isDisposed = true;
        }

        using var tokenSource = new CancellationTokenSource(_shutdownTimeout);

        try
        {
            await ShutdownAsync(
                "server connection disposed",
                cancelDispatches: true,
                abortInvocations: true,
                tokenSource.Token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _protocolConnection.Abort(exception);
        }

        _protocolConnectionCancellationSource.Dispose();
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
        Debug.Assert(_connectTask is null); // called at most once

        lock (_mutex)
        {
            ThrowIfDisposed();

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

            using var tokenSource = new CancellationTokenSource(_connectTimeout);

            // Even though this assignment is not atomic, it's ok because nobody can get hold of this connection before
            // the connection is established.
            NetworkConnectionInformation = await _protocolConnection.ConnectAsync(
                isServer: true,
                this,
                tokenSource.Token).ConfigureAwait(false);
        }
    }

    /// <summary>Gracefully shuts down the connection.</summary>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    internal Task ShutdownAsync(CancellationToken cancel = default) =>
        ShutdownAsync("server connection shutdown", cancel: cancel);

    /// <summary>Gracefully shuts down the connection.</summary>
    /// <param name="message">The message transmitted to the client when using the IceRPC protocol.</param>
    /// <param name="cancelDispatches">When <c>true</c>, cancel outstanding dispatches.</param>
    /// <param name="abortInvocations">When <c>true</c>, abort outstanding invocations.</param>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    internal async Task ShutdownAsync(
        string message,
        bool cancelDispatches = false,
        bool abortInvocations = false,
        CancellationToken cancel = default)
    {
        Task? connectTask = null;
        lock (_mutex)
        {
            ThrowIfDisposed();

            _isShutdown = true;
            connectTask = _connectTask;
        }

        if (connectTask is not null)
        {
            // Wait for connection establishment to complete before proceeding.
            try
            {
                await connectTask.WaitAsync(cancel).ConfigureAwait(false);
            }
            catch (OperationCanceledException exception) when (exception.CancellationToken != cancel)
            {
                throw new ConnectionCanceledException();
            }
        }

        if (cancelDispatches || abortInvocations)
        {
            // TODO: temporary
            _protocolConnectionCancellationSource.Cancel();
        }

        await _protocolConnection.ShutdownAsync(
            message,
            _protocolConnectionCancellationSource.Token).WaitAsync(cancel).ConfigureAwait(false);
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException($"{typeof(ServerConnection)}");
        }
    }
}
