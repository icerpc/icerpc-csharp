// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
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

    public void  Dispose()
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
    public ProtocolServiceCollection()
    {
        this.UseColoc();
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
    internal static IServiceCollection UseProtocol(this IServiceCollection collection, Protocol protocol) =>
        collection.AddSingleton(protocol);

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
            GetProtocolConnectionAsync<ISimpleNetworkConnection, IceOptions>(
                serviceProvider,
                connectionOptions.Dispatcher,
                isServer: false,
                connectionOptions.IceClientOptions,
                serviceProvider.GetSimpleClientConnectionAsync) :
            GetProtocolConnectionAsync<IMultiplexedNetworkConnection, IceRpcOptions>(
                serviceProvider,
                connectionOptions.Dispatcher,
                isServer: false,
                connectionOptions.IceRpcClientOptions,
                serviceProvider.GetMultiplexedClientConnectionAsync);
    }

    private static async Task<IProtocolConnection> GetProtocolConnectionAsync<T, TOptions>(
        IServiceProvider serviceProvider,
        IDispatcher dispatcher,
        bool isServer,
        TOptions? protocolOptions,
        Func<Task<T>> networkConnectionFactory)
            where T : INetworkConnection
            where TOptions : class
    {
        T networkConnection = await networkConnectionFactory();

        IProtocolConnection protocolConnection =
            await serviceProvider.GetRequiredService<IProtocolConnectionFactory<T, TOptions>>().CreateProtocolConnectionAsync(
                networkConnection,
                connectionInformation: new(),
                dispatcher,
                isServer,
                protocolOptions,
                CancellationToken.None);
        return protocolConnection;
    }

    private static Task<IProtocolConnection> GetServerProtocolConnectionAsync(
        this IServiceProvider serviceProvider)
    {
        ServerOptions serverOptions = serviceProvider.GetService<ServerOptions>() ?? new();

        return serviceProvider.GetRequiredService<Protocol>() == Protocol.Ice ?
            GetProtocolConnectionAsync<ISimpleNetworkConnection, IceOptions>(
                serviceProvider,
                serverOptions.Dispatcher,
                isServer: true,
                serverOptions.IceServerOptions,
                serviceProvider.GetSimpleServerConnectionAsync) :
            GetProtocolConnectionAsync<IMultiplexedNetworkConnection, IceRpcOptions>(
                serviceProvider,
                serverOptions.Dispatcher,
                isServer: true,
                serverOptions.IceRpcServerOptions,
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
