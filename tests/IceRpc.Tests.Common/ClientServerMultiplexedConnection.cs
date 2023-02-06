// Copyright (c) ZeroC, Inc.

using IceRpc.Transports;

namespace IceRpc.Tests.Common;

/// <summary>A helper class to connect and provide access to a client and server multiplexed connections. It also
/// ensures the connections are correctly disposed.</summary>
public sealed class ClientServerMultiplexedConnection : IAsyncDisposable
{
    public IMultiplexedConnection Client { get; }

    public IMultiplexedConnection Server
    {
        get => _server ?? throw new InvalidOperationException("server connection not initialized");
        private set => _server = value;
    }

    private readonly IListener<IMultiplexedConnection> _listener;
    private IMultiplexedConnection? _server;

    public async Task<IMultiplexedConnection> AcceptAsync(CancellationToken cancellationToken = default)
    {
        (_server, _) = await _listener.AcceptAsync(cancellationToken);
        await _server.ConnectAsync(cancellationToken);
        return _server;
    }

    public Task AcceptAndConnectAsync(CancellationToken cancellationToken = default) =>
        Task.WhenAll(AcceptAsync(cancellationToken), Client.ConnectAsync(cancellationToken));

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync();

        if (_server is not null)
        {
            await _server.DisposeAsync();
        }
        await _listener.DisposeAsync();
    }

    public ClientServerMultiplexedConnection(
        IMultiplexedConnection clientConnection,
        IListener<IMultiplexedConnection> listener)
    {
        _listener = listener;
        Client = clientConnection;
    }
}
