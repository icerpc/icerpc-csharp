// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Internal;
using IceRpc.Transports;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Buffers;
using System.Net.Security;

namespace IceRpc.Tests;

/// <summary>A helper struct to ensure the network and protocol connections are correctly disposed.</summary>
internal struct ClientServerProtocolConnection : IAsyncDisposable
{
    internal IProtocolConnection Client { get; }
    internal INetworkConnection ClientNetworkConnection { get; }
    internal IProtocolConnection Server { get; }
    internal INetworkConnection ServerNetworkConnection { get; }

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync();
        await Server.DisposeAsync();
    }

    internal ClientServerProtocolConnection(
        INetworkConnection clientNetworkConnection,
        INetworkConnection serverNetworkConnection,
        IProtocolConnection clientConnection,
        IProtocolConnection serverConnection)
    {
        ClientNetworkConnection = clientNetworkConnection;
        ServerNetworkConnection = serverNetworkConnection;
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

    internal static IServiceCollection UseServerConnectionOptions(
        this IServiceCollection collection,
        ConnectionOptions options) =>
        collection.AddSingleton(new ServerConnectionOptions(options));

    internal static IServiceCollection UseClientConnectionOptions(
        this IServiceCollection collection,
        ConnectionOptions options) =>
        collection.AddSingleton(new ClientConnectionOptions(options));

    internal static async Task<ClientServerProtocolConnection> GetClientServerProtocolConnectionAsync(
        this IServiceProvider serviceProvider)
    {
        Task<(INetworkConnection, IProtocolConnection)> serverTask =
            serviceProvider.GetServerProtocolConnectionAsync();
        (INetworkConnection clientNetworkConnection, IProtocolConnection clientProtocolConnection) =
            await serviceProvider.GetClientProtocolConnectionAsync();
        (INetworkConnection serverNetworkConnection, IProtocolConnection serverProtocolConnection) =
            await serverTask;
        return new ClientServerProtocolConnection(
            clientNetworkConnection,
            serverNetworkConnection,
            clientProtocolConnection,
            serverProtocolConnection);
    }

    internal static Connection GetInvalidConnection(this IServiceProvider serviceProvider) =>
        serviceProvider.GetRequiredService<Protocol>() == Protocol.Ice ? InvalidConnection.Ice :
            InvalidConnection.IceRpc;

    private static Task<(INetworkConnection, IProtocolConnection)> GetClientProtocolConnectionAsync(
        this IServiceProvider serviceProvider) => serviceProvider.GetRequiredService<Protocol>() == Protocol.Ice ?
            GetProtocolConnectionAsync(
                serviceProvider,
                isServer: false,
                serviceProvider.GetSimpleClientConnectionAsync) :
            GetProtocolConnectionAsync(
                serviceProvider,
                isServer: false,
                serviceProvider.GetMultiplexedClientConnectionAsync);

    private static async Task<(INetworkConnection, IProtocolConnection)> GetProtocolConnectionAsync<T>(
        IServiceProvider serviceProvider,
        bool isServer,
        Func<Task<T>> networkConnectionFactory) where T : INetworkConnection
    {
        T networkConnection = await networkConnectionFactory();
        ConnectionOptions connectionOptions = isServer ?
            serviceProvider.GetService<ServerConnectionOptions>()?.Value ?? new() :
            serviceProvider.GetService<ClientConnectionOptions>()?.Value ?? new();

        Action<Dictionary<ConnectionFieldKey, ReadOnlySequence<byte>>>? onConnect =
            connectionOptions.OnConnect == null ? null :
            fields => connectionOptions.OnConnect(
                serviceProvider.GetInvalidConnection(),
                fields,
                new FeatureCollection());

        IProtocolConnection protocolConnection =
            await serviceProvider.GetRequiredService<IProtocolConnectionFactory<T>>().CreateProtocolConnectionAsync(
                networkConnection,
                connectionInformation: new(),
                connectionOptions,
                onConnect,
                isServer,
                CancellationToken.None);
        return (networkConnection, protocolConnection);
    }

    private static Task<(INetworkConnection, IProtocolConnection)> GetServerProtocolConnectionAsync(
        this IServiceProvider serviceProvider) => serviceProvider.GetRequiredService<Protocol>() == Protocol.Ice ?
            GetProtocolConnectionAsync(
                serviceProvider,
                isServer: true,
                serviceProvider.GetSimpleServerConnectionAsync) :
            GetProtocolConnectionAsync(
                serviceProvider,
                isServer: true,
                serviceProvider.GetMultiplexedServerConnectionAsync);

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

    private sealed class ClientConnectionOptions
    {
        internal ConnectionOptions Value { get; }

        internal ClientConnectionOptions(ConnectionOptions options) => Value = options;
    }

    private sealed class ServerConnectionOptions
    {
        internal ConnectionOptions Value { get; }

        internal ServerConnectionOptions(ConnectionOptions options) => Value = options;
    }
}
