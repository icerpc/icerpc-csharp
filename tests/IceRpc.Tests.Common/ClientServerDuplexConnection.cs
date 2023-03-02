// Copyright (c) ZeroC, Inc.

using IceRpc.Transports;

namespace IceRpc.Tests.Common;

/// <summary>A helper class to connect and provide access to a client and server duplex connections. It also ensures the
/// connections are correctly disposed.</summary>
public sealed class ClientServerDuplexConnection : IAsyncDisposable
{
    /// <summary>Gets the client connection.</summary>
    public IDuplexConnection Client { get; }

    /// <summary>Gets the server connection.</summary>
    public IDuplexConnection Server
    {
        get => _server ?? throw new InvalidOperationException("server connection not initialized");
        private set => _server = value;
    }

    private readonly IListener<IDuplexConnection> _listener;
    private IDuplexConnection? _server;

    /// <summary>Accepts and connects the server connection.</summary>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The accepted server connection and connection information.</returns>
    public async Task<TransportConnectionInformation> AcceptAsync(CancellationToken cancellationToken = default)
    {
        (_server, _) = await _listener.AcceptAsync(cancellationToken);
        return await _server.ConnectAsync(cancellationToken);
    }

    /// <summary>Connects the client connection and accepts and connects the server connection.</summary>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A task that completes when both connections are connected.</returns>
    public Task AcceptAndConnectAsync(CancellationToken cancellationToken = default) =>
        Task.WhenAll(AcceptAsync(cancellationToken), Client.ConnectAsync(cancellationToken));

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        Client.Dispose();
        _server?.Dispose();
        return _listener.DisposeAsync();
    }

    /// <summary>Constructs a new <see cref="ClientServerDuplexConnection"/>.</summary>
    /// <param name="clientConnection">The client connection.</param>
    /// <param name="listener">The listener.</param>
    public ClientServerDuplexConnection(IDuplexConnection clientConnection, IListener<IDuplexConnection> listener)
    {
        _listener = listener;
        Client = clientConnection;
    }
}
