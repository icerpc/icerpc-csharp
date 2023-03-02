// Copyright (c) ZeroC, Inc.

using IceRpc.Internal;
using IceRpc.Tests.Common;
using IceRpc.Tests.Transports;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;

namespace IceRpc.Tests;

public static class ProtocolServiceCollectionExtensions
{
    public static IServiceCollection AddProtocolTest(
        this IServiceCollection services,
        Protocol protocol,
        IDispatcher? dispatcher = null,
        ConnectionOptions? clientConnectionOptions = null,
        ConnectionOptions? serverConnectionOptions = null)
    {
        clientConnectionOptions ??= new();
        clientConnectionOptions.Dispatcher ??= ServiceNotFoundDispatcher.Instance;
        serverConnectionOptions ??= new();
        serverConnectionOptions.Dispatcher ??= dispatcher ?? ServiceNotFoundDispatcher.Instance;

        if (protocol == Protocol.Ice)
        {
            services
                .AddColocTransport()
                .AddDuplexTransportTest()
                .AddSingleton(provider =>
                    new ClientServerProtocolConnection(
                        clientProtocolConnection: new IceProtocolConnection(
                            provider.GetRequiredService<ClientServerDuplexConnection>().Client,
                            transportConnectionInformation: null,
                            clientConnectionOptions ?? new()),
                        acceptServerConnectionAsync:
                            async (CancellationToken cancellationToken) =>
                            {
                                IDuplexConnection transportConnection;
                                TransportConnectionInformation transportConnectionInformation;
                                (transportConnection, transportConnectionInformation) =
                                    await provider.GetRequiredService<ClientServerDuplexConnection>().AcceptAsync(
                                        cancellationToken);

                                return new IceProtocolConnection(
                                    transportConnection,
                                    transportConnectionInformation,
                                    serverConnectionOptions ?? new());
                            }));
        }
        else
        {
            services
                .AddColocTransport()
                .AddSlicTransport()
                .AddMultiplexedTransportTest()
                .AddSingleton(provider =>
                    new ClientServerProtocolConnection(
                        clientProtocolConnection: new IceRpcProtocolConnection(
                            provider.GetRequiredService<ClientServerMultiplexedConnection>().Client,
                            transportConnectionInformation: null,
                            clientConnectionOptions ?? new(),
                            provider.GetService<ITaskExceptionObserver>()),
                        acceptServerConnectionAsync:
                            async (CancellationToken cancellationToken) =>
                            {
                                IMultiplexedConnection transportConnection;
                                TransportConnectionInformation transportConnectionInformation;
                                (transportConnection, transportConnectionInformation) =
                                    await provider.GetRequiredService<ClientServerMultiplexedConnection>().AcceptAsync(
                                        cancellationToken);

                                return new IceRpcProtocolConnection(
                                    transportConnection,
                                    transportConnectionInformation,
                                    serverConnectionOptions ?? new(),
                                    provider.GetService<ITaskExceptionObserver>());
                            }));
        }
        return services;
    }
}

/// <summary>A helper class to connect and provide access to a client and server protocol connection. It also ensures
/// the connections are correctly disposed.</summary>
internal sealed class ClientServerProtocolConnection : IAsyncDisposable
{
    public IProtocolConnection Client { get; }

    public IProtocolConnection Server
    {
        get => _server ?? throw new InvalidOperationException("server connection not initialized");
        private set => _server = value;
    }

    private readonly Func<CancellationToken, Task<IProtocolConnection>> _acceptServerConnectionAsync;
    private IProtocolConnection? _server;

    public async Task<(Task ClientShutdownRequested, Task ServerShutdownRequested)> ConnectAsync(
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

    public async Task<Task> AcceptAsync(CancellationToken cancellationToken = default)
    {
        _server = await _acceptServerConnectionAsync(cancellationToken);
        return (await _server.ConnectAsync(cancellationToken)).ShutdownRequested;
    }

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
