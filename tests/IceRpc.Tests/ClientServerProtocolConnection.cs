// Copyright (c) ZeroC, Inc.

using IceRpc.Internal;
using IceRpc.Tests.Common;
using IceRpc.Tests.Transports;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;

namespace IceRpc.Tests;

/// <summary>A helper class to connect and provide access to a client and server protocol connection. It also ensures
/// the connections are correctly disposed.</summary>
internal sealed class ClientServerProtocolConnection : IAsyncDisposable
{
    /// <summary>Gets the client connection.</summary>
    internal IProtocolConnection Client { get; }

    /// <summary>Gets the server connection.</summary>
    internal IProtocolConnection Server
    {
        get => _server ?? throw new InvalidOperationException("server connection not initialized");
        private set => _server = value;
    }

    private readonly Func<CancellationToken, Task<IProtocolConnection>> _acceptServerConnectionAsync;
    private IProtocolConnection? _server;

    /// <summary>Accepts and connects the server connection.</summary>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The shutdown requested task returned by <see cref="IProtocolConnection.ConnectAsync"/>.</returns>
    internal async Task<Task> AcceptAsync(CancellationToken cancellationToken = default)
    {
        _server = await _acceptServerConnectionAsync(cancellationToken);
        return (await _server.ConnectAsync(cancellationToken)).ShutdownRequested;
    }

    /// <summary>Connects the client connection and accepts and connects the server connection.</summary>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The client and server shutdown requested tasks returned by <see
    /// cref="IProtocolConnection.ConnectAsync"/>.</returns>
    internal async Task<(Task ClientShutdownRequested, Task ServerShutdownRequested)> ConnectAsync(
        CancellationToken cancellationToken = default)
    {
        Task<(TransportConnectionInformation ConnectionInformation, Task ShutdownRequested)> clientProtocolConnectionTask =
            Client.ConnectAsync(cancellationToken);

        Task serverShutdownRequested;
        try
        {
            serverShutdownRequested = await AcceptAsync(cancellationToken);
        }
        catch
        {
            await Client.DisposeAsync();
            try
            {
                await clientProtocolConnectionTask;
            }
            catch
            {
            }
            throw;
        }
        return ((await clientProtocolConnectionTask).ShutdownRequested, serverShutdownRequested);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync();

        if (_server is not null)
        {
            await _server.DisposeAsync();
        }
    }

    internal ClientServerProtocolConnection(
        IProtocolConnection clientProtocolConnection,
        Func<CancellationToken, Task<IProtocolConnection>> acceptServerConnectionAsync)
    {
        _acceptServerConnectionAsync = acceptServerConnectionAsync;
        Client = clientProtocolConnection;
    }
}
