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

    private readonly IProtocolConnection _protocolConnection;

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => _protocolConnection.DisposeAsync();

    /// <inheritdoc/>
    public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel) =>
        _protocolConnection.InvokeAsync(request, this, cancel);

    /// <inheritdoc/>
    public void OnClose(Action<Exception> callback) => _protocolConnection.OnClose(callback);

    /// <summary>Constructs a server connection from an accepted network connection.</summary>
    internal ServerConnection(IProtocolConnection protocolConnection, ConnectionOptions options) =>
        _protocolConnection = new ProtocolConnectionStateDecorator(
            protocolConnection,
            options.ConnectTimeout,
            options.ShutdownTimeout);

    /// <summary>Aborts the connection.</summary>
    internal void Abort() => _protocolConnection.Abort(new ConnectionAbortedException());

    /// <summary>Establishes the connection.</summary>
    /// <returns>A task that indicates the completion of the connect operation.</returns>
    internal Task ConnectAsync() =>
        _protocolConnection.ConnectAsync(isServer: true, connection: this, cancel: CancellationToken.None);

    /// <summary>Gracefully shuts down the connection.</summary>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    internal Task ShutdownAsync(CancellationToken cancel = default) =>
        ShutdownAsync("server connection shutdown", cancel);

    /// <summary>Gracefully shuts down the connection.</summary>
    /// <param name="message">The message transmitted to the client when using the IceRPC protocol.</param>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    internal Task ShutdownAsync(string message, CancellationToken cancel = default) =>
        _protocolConnection.ShutdownAsync(message, cancel);
}
