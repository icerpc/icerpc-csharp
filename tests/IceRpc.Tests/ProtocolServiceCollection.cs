// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Tests.Common;
using IceRpc.Transports;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net.Security;

namespace IceRpc.Tests;

/// <summary>A helper struct to ensure the network and protocol connections are correctly disposed.</summary>
internal struct ClientServerProtocolConnection : IDisposable
{
    internal IProtocolConnection Client { get; }
    internal IProtocolConnection Server { get; }

    public void Dispose()
    {
        Client.Dispose();
        Server.Dispose();
    }

    internal ClientServerProtocolConnection(IProtocolConnection clientConnection, IProtocolConnection serverConnection)
    {
        Client = clientConnection;
        Server = serverConnection;
    }
}

public static class ProtocolServiceCollectionExtensions
{
    public static IServiceCollection AddProtocolTest(this IServiceCollection services, Protocol protocol)
    {
        var endpoint = new Endpoint(protocol) { Host = "colochost" };
        services.AddColocTransport();
        services.AddSingleton(protocol);

        services.TryAddSingleton<IConnection>(
            protocol == Protocol.Ice ? InvalidConnection.Ice : InvalidConnection.IceRpc);

        services.AddSingleton<IServerTransport<IMultiplexedNetworkConnection>>(
            provider => new SlicServerTransport(
                provider.GetRequiredService<IServerTransport<ISimpleNetworkConnection>>()));
        services.AddSingleton<IClientTransport<IMultiplexedNetworkConnection>>(
            provider => new SlicClientTransport(
                provider.GetRequiredService<IClientTransport<ISimpleNetworkConnection>>()));

        services.AddSingleton(IceProtocol.Instance.ProtocolConnectionFactory);
        services.AddSingleton(IceRpcProtocol.Instance.ProtocolConnectionFactory);
        services.AddSingleton(provider => CreateListener<ISimpleNetworkConnection>(provider, endpoint));
        services.AddSingleton(provider => CreateListener<IMultiplexedNetworkConnection>(provider, endpoint));

        return services;

        static IListener<T> CreateListener<T>(IServiceProvider serviceProvider, Endpoint endpoint)
            where T : INetworkConnection
        {
            ILogger logger = serviceProvider.GetService<ILogger>() ?? NullLogger.Instance;

            IListener<T> listener =
                serviceProvider.GetRequiredService<IServerTransport<T>>().Listen(
                    endpoint,
                    serviceProvider.GetService<SslServerAuthenticationOptions>(),
                    logger);

            if (logger != NullLogger.Instance)
            {
                LogNetworkConnectionDecoratorFactory<T>? decorator =
                    serviceProvider.GetService<LogNetworkConnectionDecoratorFactory<T>>();
                if (decorator != null)
                {
                    listener = new LogListenerDecorator<T>(listener, logger, decorator);
                }
            }
            return listener;
        }
    }
}

internal static class ProtocolTests
{
    internal static async Task<ClientServerProtocolConnection> CreateClientServerProtocolConnectionAsync<T>(
        IProtocolConnectionFactory<T> protocolConnectionFactory,
        IClientTransport<T> clientTransport,
        IListener<T> listener,
        IConnection connection,
        ServerOptions? serverOptions = null,
        bool acceptRequests = true) where T : INetworkConnection
    {
        T clientNetworkConnection = clientTransport.CreateConnection(listener.Endpoint, null, NullLogger.Instance);
        var connectTask = clientNetworkConnection.ConnectAsync(CancellationToken.None);
        T serverNetworkConnection = await listener.AcceptAsync();
        await serverNetworkConnection.ConnectAsync(CancellationToken.None);
        await connectTask;

        var serverTask = protocolConnectionFactory.CreateProtocolConnectionAsync(
            serverNetworkConnection,
            networkConnectionInformation: new(),
            true,
            serverOptions?.ConnectionOptions ?? new ConnectionOptions(),
            CancellationToken.None);

        IProtocolConnection clientProtocolConnection = await protocolConnectionFactory.CreateProtocolConnectionAsync(
            clientNetworkConnection,
            networkConnectionInformation: new(),
            false,
            new ConnectionOptions(),
            CancellationToken.None);

        IProtocolConnection serverProtocolConnection = await serverTask;

        if (acceptRequests)
        {
            _ = clientProtocolConnection.AcceptRequestsAsync(connection);
            _ = serverProtocolConnection.AcceptRequestsAsync(connection);
        }

        return new ClientServerProtocolConnection(clientProtocolConnection, serverProtocolConnection);
    }
}
