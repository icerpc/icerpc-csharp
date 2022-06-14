// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc.Internal;

/// <summary>A connection created by a <see cref="Server"/>.</summary>
internal sealed class ServerConnection : IConnection
{
    /// <inheritdoc/>
    public bool IsInvocable => _isInvocable;

    /// <inheritdoc/>
    public NetworkConnectionInformation? NetworkConnectionInformation => _core.NetworkConnectionInformation;

    /// <inheritdoc/>
    public Protocol Protocol { get; }

    private readonly ConnectionCore _core;

    private volatile bool _isInvocable = true;

    /// <inheritdoc/>
    public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel) =>
        _core.InvokeAsync(this, request, cancel);

    /// <inheritdoc/>
    public void OnClose(Action<IConnection, Exception> callback) => _core.OnClose(this, callback);

    /// <summary>Constructs a server connection from an accepted network connection.</summary>
    internal ServerConnection(Protocol protocol, ConnectionOptions options)
    {
        Protocol = protocol;
        _core = new ConnectionCore(options);
        _core.OnClose(this, static (connection, exception) =>
        {
            var serverConnection = (ServerConnection)connection;
            serverConnection._isInvocable = false;
        });
    }

    /// <summary>Aborts the connection.</summary>
    internal void Abort() => _core.Abort(this);

    /// <summary>Establishes a connection.</summary>
    /// <param name="networkConnection">The underlying network connection.</param>
    /// <param name="protocolConnectionFactory">The protocol connection factory.</param>
    internal Task ConnectAsync<T>(T networkConnection, IProtocolConnectionFactory<T> protocolConnectionFactory)
        where T : INetworkConnection =>
        _core.ConnectServerAsync(this, networkConnection, protocolConnectionFactory);

    /// <summary>Gracefully shuts down of the connection. If ShutdownAsync is canceled, dispatch and invocations are
    /// canceled. Shutdown cancellation can lead to a speedier shutdown if dispatch are cancelable.</summary>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    internal Task ShutdownAsync(CancellationToken cancel = default) => ShutdownAsync("connection shutdown", cancel);

    /// <summary>Gracefully shuts down of the connection. If ShutdownAsync is canceled, dispatch and invocations are
    /// canceled. Shutdown cancellation can lead to a speedier shutdown if dispatch are cancelable.</summary>
    /// <param name="message">The message transmitted to the peer (when using the IceRPC protocol).</param>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    internal Task ShutdownAsync(string message, CancellationToken cancel = default) =>
        _core.ShutdownAsync(this, message, cancel);
}
