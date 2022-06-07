// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc;
using IceRpc.Extensions.DependencyInjection.Builder;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Extension methods for setting up IceRpc services in an <see cref="IServiceCollection"/>.</summary>
public static class IceRpcServiceCollectionExtensions
{
    /// <summary>Adds a <see cref="Server"/> with name <paramref name="serverName"/> to this service collection.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="serverName">The server name.</param>
    public static IServiceCollection AddIceRpcServer(this IServiceCollection services, string serverName) =>
        services
            .TryAddIceRpcServerTransport()
            .AddSingleton<Server>(provider =>
                new Server(
                    provider.GetRequiredService<IOptionsMonitor<ServerOptions>>().Get(serverName),
                    loggerFactory: provider.GetService<ILoggerFactory>(),
                    provider.GetRequiredService<IServerTransport<IMultiplexedNetworkConnection>>(),
                    provider.GetRequiredService<IServerTransport<ISimpleNetworkConnection>>()));

    /// <summary>Adds a <see cref="Server"/> with the default name ("") to this service collection.</summary>
    public static IServiceCollection AddIceRpcServer(this IServiceCollection services) =>
        services.AddIceRpcServer(Options.Options.DefaultName);

    /// <summary>Adds a <see cref="Server"/> with the specified name and dispatcher to this service collection.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="serverName">The server name.</param>
    /// <param name="dispatcher">The server dispatcher.</param>
    public static IServiceCollection AddIceRpcServer(
        this IServiceCollection services,
        string serverName,
        IDispatcher dispatcher)
    {
        services.AddOptions<ServerOptions>(serverName).Configure(
            options => options.ConnectionOptions.Dispatcher = dispatcher);
        return services.AddIceRpcServer(serverName);
    }

    /// <summary>Adds a <see cref="Server"/> with the specified dispatcher and default name ("") to this service
    /// collection.</summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="dispatcher">The server dispatcher.</param>
    public static IServiceCollection AddIceRpcServer(this IServiceCollection services, IDispatcher dispatcher) =>
        services.AddIceRpcServer(serverName: Options.Options.DefaultName, dispatcher);

    /// <summary>Adds a <see cref="Server"/> with the specified name to this service collection.</summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="serverName">The server name.</param>
    /// <param name="configure">The action to configure the dispatcher using a <see cref="DispatcherBuilder"/>.</param>
    public static IServiceCollection AddIceRpcServer(
        this IServiceCollection services,
        string serverName,
        Action<IDispatcherBuilder> configure) =>
        services
            .TryAddIceRpcServerTransport()
            .AddSingleton<Server>(provider =>
            {
                var dispatcherBuilder = new DispatcherBuilder(provider);
                configure(dispatcherBuilder);

                ServerOptions options = provider.GetRequiredService<IOptionsMonitor<ServerOptions>>().Get(serverName);
                options.ConnectionOptions.Dispatcher = dispatcherBuilder.Build();

                return new Server(
                    options,
                    loggerFactory: provider.GetService<ILoggerFactory>(),
                    provider.GetRequiredService<IServerTransport<IMultiplexedNetworkConnection>>(),
                    provider.GetRequiredService<IServerTransport<ISimpleNetworkConnection>>());
            });

    /// <summary>Adds a <see cref="Server"/> with the default name ("") to this service collection.</summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="configure">The action to configure the dispatcher using a <see cref="DispatcherBuilder"/>.</param>
    public static IServiceCollection AddIceRpcServer(
        this IServiceCollection services,
        Action<IDispatcherBuilder> configure) =>
        services.AddIceRpcServer(serverName: Options.Options.DefaultName, configure);

    /// <summary>Adds <see cref="ClientConnection"/> to this service collection.</summary>
    /// <param name="services">The service collection to add services to.</param>
    public static IServiceCollection AddIceRpcClientConnection(this IServiceCollection services) =>
        services
            .TryAddIceRpcClientTransport()
            // TODO should this be IClientConnection?
            .AddSingleton<ClientConnection>(provider =>
                new ClientConnection(
                    provider.GetRequiredService<IOptions<ClientConnectionOptions>>().Value,
                    loggerFactory: provider.GetService<ILoggerFactory>(),
                    provider.GetRequiredService<IClientTransport<IMultiplexedNetworkConnection>>(),
                    provider.GetRequiredService<IClientTransport<ISimpleNetworkConnection>>()));

    /// <summary>Adds <see cref="ConnectionPool"/> connection provider to this service collection.</summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <seealso cref="IClientConnectionProvider"/>
    public static IServiceCollection AddIceRpcConnectionPool(this IServiceCollection services) =>
        services
            .TryAddIceRpcClientTransport()
            .AddSingleton<IClientConnectionProvider>(provider =>
                new ConnectionPool(
                    provider.GetRequiredService<IOptions<ConnectionPoolOptions>>().Value,
                    loggerFactory: provider.GetService<ILoggerFactory>(),
                    provider.GetRequiredService<IClientTransport<IMultiplexedNetworkConnection>>(),
                    provider.GetRequiredService<IClientTransport<ISimpleNetworkConnection>>()));

    private static IServiceCollection TryAddIceRpcServerTransport(this IServiceCollection services)
    {
        services
           .AddOptions()
           .TryAddSingleton<IServerTransport<ISimpleNetworkConnection>>(
               provider => new TcpServerTransport(
                   provider.GetRequiredService<IOptions<TcpServerTransportOptions>>().Value));

        services
            .AddOptions()
            .TryAddSingleton<IServerTransport<IMultiplexedNetworkConnection>>(
                provider => new SlicServerTransport(
                    provider.GetRequiredService<IOptions<SlicTransportOptions>>().Value,
                    provider.GetRequiredService<IServerTransport<ISimpleNetworkConnection>>()));

        return services;
    }

    private static IServiceCollection TryAddIceRpcClientTransport(this IServiceCollection services)
    {
        services
            .AddOptions()
            .TryAddSingleton<IClientTransport<ISimpleNetworkConnection>>(
                provider => new TcpClientTransport(
                    provider.GetRequiredService<IOptions<TcpClientTransportOptions>>().Value));

        services.
            TryAddSingleton<IClientTransport<IMultiplexedNetworkConnection>>(
                provider => new SlicClientTransport(
                    provider.GetRequiredService<IOptions<SlicTransportOptions>>().Value,
                    provider.GetRequiredService<IClientTransport<ISimpleNetworkConnection>>()));

        return services;
    }
}
