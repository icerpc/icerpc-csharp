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
    public static IServiceCollection AddProtocolTest(
        this IServiceCollection services,
        Protocol protocol,
        IDispatcher? dispatcher = null)
    {
        var endpoint = new Endpoint(protocol) { Host = "colochost" };
        services.AddColocTransport();
        services.AddSingleton(protocol);

        if (dispatcher != null)
        {
            services.AddOptions<ServerOptions>().Configure(
                options =>
                {
                    options.ConnectionOptions = new ConnectionOptions { Dispatcher = dispatcher };
                });
        }

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

internal static class ProtocolServiceProviderExtensions
{
    internal static async Task<ClientServerProtocolConnection> GetClientServerProtocolConnectionAsync(
        this IServiceProvider serviceProvider,
        Protocol protocol,
        bool acceptRequests = true)
    {
        Task<IProtocolConnection> serverTask;
        ServerOptions serverOptions = serviceProvider.GetService<IOptions<ServerOptions>>()?.Value ?? new();
        if (protocol == Protocol.Ice)
        {
            serverTask = GetProtocolConnectionAsync(
                serviceProvider,
                isServer: true,
                serverOptions.ConnectionOptions,
                serviceProvider.GetSimpleServerConnectionAsync);
        }
        else
        {
            serverTask = GetProtocolConnectionAsync(
                serviceProvider,
                isServer: true,
                serverOptions.ConnectionOptions,
                serviceProvider.GetMultiplexedServerConnectionAsync);
        }

        IProtocolConnection clientProtocolConnection;
        ConnectionOptions connectionOptions = serviceProvider.GetService<ConnectionOptions>() ?? new();
        if (protocol == Protocol.Ice)
        {
            clientProtocolConnection = await GetProtocolConnectionAsync<ISimpleNetworkConnection>(
                serviceProvider,
                isServer: false,
                connectionOptions,
                serviceProvider.GetSimpleClientConnectionAsync);
        }
        else
        {
            clientProtocolConnection = await GetProtocolConnectionAsync<IMultiplexedNetworkConnection>(
                serviceProvider,
                isServer: false,
                connectionOptions,
                serviceProvider.GetMultiplexedClientConnectionAsync);
        }

        IProtocolConnection serverProtocolConnection = await serverTask;

        if (acceptRequests)
        {
            _ = clientProtocolConnection.AcceptRequestsAsync(serviceProvider.GetRequiredService<IConnection>());
            _ = serverProtocolConnection.AcceptRequestsAsync(serviceProvider.GetRequiredService<IConnection>());
        }

        return new ClientServerProtocolConnection(clientProtocolConnection, serverProtocolConnection);
    }

    private static async Task<IProtocolConnection> GetProtocolConnectionAsync<T>(
        IServiceProvider serviceProvider,
        bool isServer,
        ConnectionOptions connectionOptions,
        Func<Task<T>> networkConnectionFactory)
            where T : INetworkConnection
    {
        T networkConnection = await networkConnectionFactory();

        IProtocolConnection protocolConnection =
            await serviceProvider.GetRequiredService<IProtocolConnectionFactory<T>>().CreateProtocolConnectionAsync(
                networkConnection,
                connectionInformation: new(),
                isServer,
                connectionOptions,
                CancellationToken.None);
        return protocolConnection;
    }

    public static Task<IMultiplexedNetworkConnection> GetMultiplexedClientConnectionAsync(
            this IServiceProvider serviceProvider) =>
            GetClientNetworkConnectionAsync<IMultiplexedNetworkConnection>(serviceProvider);

    public static Task<IMultiplexedNetworkConnection> GetMultiplexedServerConnectionAsync(
        this IServiceProvider serviceProvider) =>
        GetServerNetworkConnectionAsync<IMultiplexedNetworkConnection>(serviceProvider);

    public static Task<ISimpleNetworkConnection> GetSimpleClientConnectionAsync(
        this IServiceProvider serviceProvider) =>
        GetClientNetworkConnectionAsync<ISimpleNetworkConnection>(serviceProvider);

    public static Task<ISimpleNetworkConnection> GetSimpleServerConnectionAsync(
        this IServiceProvider serviceProvider) =>
        GetServerNetworkConnectionAsync<ISimpleNetworkConnection>(serviceProvider);

    private static async Task<T> GetClientNetworkConnectionAsync<T>(
        IServiceProvider serviceProvider) where T : INetworkConnection
    {
        Endpoint endpoint = serviceProvider.GetRequiredService<IListener<T>>().Endpoint;
        ILogger logger = serviceProvider.GetService<ILogger>() ?? NullLogger.Instance;
        T connection = serviceProvider.GetRequiredService<IClientTransport<T>>().CreateConnection(
            endpoint,
            serviceProvider.GetService<SslClientAuthenticationOptions>(),
            logger);
        if (logger != NullLogger.Instance)
        {
            LogNetworkConnectionDecoratorFactory<T>? decorator =
                serviceProvider.GetService<LogNetworkConnectionDecoratorFactory<T>>();
            if (decorator != null)
            {
                connection = decorator(connection, endpoint, false, logger);
            }
        }
        await connection.ConnectAsync(default);
        return connection;
    }

    private static async Task<T> GetServerNetworkConnectionAsync<T>(
        IServiceProvider serviceProvider) where T : INetworkConnection
    {
        IListener<T> listener = serviceProvider.GetRequiredService<IListener<T>>();
        T connection = await listener.AcceptAsync();
        await connection.ConnectAsync(default);
        return connection;
    }
}
