// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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

internal class ProtocolServiceCollection : ServiceCollection
{
    public ProtocolServiceCollection(Protocol protocol)
    {
        this.AddColocTransport();
        this.AddSingleton(protocol);
        this.AddSingleton(typeof(Endpoint), new Endpoint(protocol) { Host = "colochost" });
        this.AddScoped<IServerTransport<IMultiplexedNetworkConnection>>(
            provider => new SlicServerTransport(
                provider.GetRequiredService<IServerTransport<ISimpleNetworkConnection>>()));
        this.AddScoped<IClientTransport<IMultiplexedNetworkConnection>>(
            provider => new SlicClientTransport(
                provider.GetRequiredService<IClientTransport<ISimpleNetworkConnection>>()));

        this.AddSingleton(IceProtocol.Instance.ProtocolConnectionFactory);
        this.AddSingleton(IceRpcProtocol.Instance.ProtocolConnectionFactory);
        this.AddScoped(provider => CreateListener<ISimpleNetworkConnection>(provider));
        this.AddScoped(provider => CreateListener<IMultiplexedNetworkConnection>(provider));

        static IListener<T> CreateListener<T>(IServiceProvider serviceProvider) where T : INetworkConnection
        {
            ILogger logger = serviceProvider.GetService<ILogger>() ?? NullLogger.Instance;

            IListener<T> listener =
                serviceProvider.GetRequiredService<IServerTransport<T>>().Listen(
                    serviceProvider.GetRequiredService<Endpoint>(),
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

internal static class ProtocolServiceCollectionExtensions
{
    internal static async Task<ClientServerProtocolConnection> GetClientServerProtocolConnectionAsync(
        this IServiceProvider serviceProvider,
        bool acceptRequests = true)
    {
        Task<IProtocolConnection> serverTask = serviceProvider.GetServerProtocolConnectionAsync();
        IProtocolConnection clientProtocolConnection = await serviceProvider.GetClientProtocolConnectionAsync();
        IProtocolConnection serverProtocolConnection = await serverTask;

        if (acceptRequests)
        {
            _ = clientProtocolConnection.AcceptRequestsAsync(serviceProvider.GetInvalidConnection());
            _ = serverProtocolConnection.AcceptRequestsAsync(serviceProvider.GetInvalidConnection());
        }

        return new ClientServerProtocolConnection(clientProtocolConnection, serverProtocolConnection);
    }

    internal static IConnection GetInvalidConnection(this IServiceProvider serviceProvider) =>
        serviceProvider.GetRequiredService<Protocol>() == Protocol.Ice ? InvalidConnection.Ice :
            InvalidConnection.IceRpc;

    private static Task<IProtocolConnection> GetClientProtocolConnectionAsync(
        this IServiceProvider serviceProvider)
    {
        ConnectionOptions connectionOptions = serviceProvider.GetService<ConnectionOptions>() ?? new();

        return serviceProvider.GetRequiredService<Protocol>() == Protocol.Ice ?
            GetProtocolConnectionAsync<ISimpleNetworkConnection>(
                serviceProvider,
                connectionOptions.Dispatcher,
                isServer: false,
                connectionOptions,
                serviceProvider.GetSimpleClientConnectionAsync) :
            GetProtocolConnectionAsync<IMultiplexedNetworkConnection>(
                serviceProvider,
                connectionOptions.Dispatcher,
                isServer: false,
                connectionOptions,
                serviceProvider.GetMultiplexedClientConnectionAsync);
    }

    private static async Task<IProtocolConnection> GetProtocolConnectionAsync<T>(
        IServiceProvider serviceProvider,
        IDispatcher dispatcher,
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
                dispatcher,
                isServer,
                connectionOptions,
                CancellationToken.None);
        return protocolConnection;
    }

    private static Task<IProtocolConnection> GetServerProtocolConnectionAsync(
        this IServiceProvider serviceProvider)
    {
        ServerOptions serverOptions = serviceProvider.GetService<ServerOptions>() ?? new();

        return serviceProvider.GetRequiredService<Protocol>() == Protocol.Ice ?
            GetProtocolConnectionAsync(
                serviceProvider,
                serverOptions.ConnectionOptions.Dispatcher,
                isServer: true,
                serverOptions.ConnectionOptions,
                serviceProvider.GetSimpleServerConnectionAsync) :
            GetProtocolConnectionAsync(
                serviceProvider,
                serverOptions.ConnectionOptions.Dispatcher,
                isServer: true,
                serverOptions.ConnectionOptions,
                serviceProvider.GetMultiplexedServerConnectionAsync);
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
