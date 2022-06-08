// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System.Diagnostics;

namespace IceRpc.Internal;

/// <summary>A connection created by a <see cref="Server"/>.</summary>
internal class ServerConnection : IConnection, IAsyncDisposable
{
    /// <inheritdoc/>
    public bool IsInvocable => Common.IsInvocable;

    /// <inheritdoc/>
    public NetworkConnectionInformation? NetworkConnectionInformation => Common.NetworkConnectionInformation;

    /// <inheritdoc/>
    public Protocol Protocol { get; }

    internal CommonConnection Common { get; }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        // Perform a speedy graceful shutdown by canceling invocations and dispatches in progress.
        await ShutdownAsync("connection disposed", new CancellationToken(canceled: true)).ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc/>
    public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel) =>
        Common.InvokeAsync(this, request, cancel);

    /// <summary>Constructs a server connection from an accepted network connection.</summary>
    internal ServerConnection(Protocol protocol, ConnectionOptions options)
    {
        Protocol = protocol;
        Common = new CommonConnection(ConnectionState.Connecting, isServer: true, options, isResumable: false);
    }

    /// <summary>Aborts the connection. This method switches the connection state to <see
    /// cref="ConnectionState.Closed"/>.</summary>
    internal void Abort() => Common.Abort(this);

    /// <summary>Establishes a connection.</summary>
    /// <param name="networkConnection">The underlying network connection.</param>
    /// <param name="protocolConnectionFactory">The protocol connection factory.</param>
    /// <param name="onClose">An action to execute when the connection is closed.</param>
    internal Task ConnectAsync<T>(
        T networkConnection,
        IProtocolConnectionFactory<T> protocolConnectionFactory,
        Action<IConnection, Exception>? onClose) where T : INetworkConnection =>
        Common.ConnectAsync(this, networkConnection, protocolConnectionFactory, onClose);

    /// <summary>Gracefully shuts down of the connection. If ShutdownAsync is canceled, dispatch and invocations are
    /// canceled. Shutdown cancellation can lead to a speedier shutdown if dispatch are cancelable.</summary>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    internal Task ShutdownAsync(CancellationToken cancel = default) => ShutdownAsync("connection shutdown", cancel);

    /// <summary>Gracefully shuts down of the connection. If ShutdownAsync is canceled, dispatch and invocations are
    /// canceled. Shutdown cancellation can lead to a speedier shutdown if dispatch are cancelable.</summary>
    /// <param name="message">The message transmitted to the peer (when using the IceRPC protocol).</param>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    internal Task ShutdownAsync(string message, CancellationToken cancel = default) =>
        Common.ShutdownAsync(this, message, isResumable: false, cancel);
}
