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
    /// <summary>Adds <see cref="Server"/> to the given <see cref="IServiceCollection"/>.</summary>
    public static IServiceCollection AddIceRpcServer(this IServiceCollection services) =>
        services
            .AddIceRpcServerTransport()
            .AddSingleton<Server>(provider =>
                new Server(
                    provider.GetRequiredService<IOptions<ServerOptions>>().Value,
                    loggerFactory: provider.GetService<ILoggerFactory>(),
                    provider.GetRequiredService<IServerTransport<IMultiplexedNetworkConnection>>(),
                    provider.GetRequiredService<IServerTransport<ISimpleNetworkConnection>>()));

    /// <summary>Adds <see cref="Server"/> with the specified dispatcher to the given <see cref="IServiceCollection"/>.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="dispatcher">The server dispatcher.</param>
    public static IServiceCollection AddIceRpcServer(this IServiceCollection services, IDispatcher dispatcher)
    {
        services.AddOptions<ServerOptions>().Configure(options => options.ConnectionOptions.Dispatcher = dispatcher);
        return services.AddIceRpcServer();
    }

    /// <summary>Adds <see cref="Server"/> with the specified dispatcher to the given <see cref="IServiceCollection"/>.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="configure">The action to configure the dispatcher using a <see cref="DispatcherBuilder"/>.</param>
    public static IServiceCollection AddIceRpcServer(
        this IServiceCollection services,
        Action<DispatcherBuilder> configure) =>
        services
            .AddIceRpcServerTransport()
            .AddSingleton<Server>(provider =>
            {
                var dispatcherBuilder = new DispatcherBuilder(provider);
                configure(dispatcherBuilder);

                services.AddOptions<ServerOptions>().Configure(
                    options => options.ConnectionOptions.Dispatcher = dispatcherBuilder.Build());

                return new Server(
                    provider.GetRequiredService<IOptions<ServerOptions>>().Value,
                    loggerFactory: provider.GetService<ILoggerFactory>(),
                    provider.GetRequiredService<IServerTransport<IMultiplexedNetworkConnection>>(),
                    provider.GetRequiredService<IServerTransport<ISimpleNetworkConnection>>());
            });

    /// <summary>Adds <see cref="ClientConnection"/> to the given <see cref="IServiceCollection"/>.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    public static IServiceCollection AddIceRpcClientConnection(this IServiceCollection services) =>
        services
            .AddIceRpcClientTransport()
            // TODO should this be IClientConnection
            .AddSingleton<ClientConnection>(provider =>
                new ClientConnection(
                    provider.GetRequiredService<IOptions<ClientConnectionOptions>>().Value,
                    loggerFactory: provider.GetService<ILoggerFactory>(),
                    provider.GetRequiredService<IClientTransport<IMultiplexedNetworkConnection>>(),
                    provider.GetRequiredService<IClientTransport<ISimpleNetworkConnection>>()));

    /// <summary>Adds <see cref="ConnectionPool"/> connection provider to the given <see cref="IServiceCollection"/>.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <seealso cref="IClientConnectionProvider"/>
    public static IServiceCollection AddIceRpcConnectionPool(this IServiceCollection services) =>
        services
            .AddIceRpcClientTransport()
            .AddSingleton<IClientConnectionProvider>(provider =>
                new ConnectionPool(
                    provider.GetRequiredService<IOptions<ConnectionPoolOptions>>().Value,
                    loggerFactory: provider.GetService<ILoggerFactory>(),
                    provider.GetRequiredService<IClientTransport<IMultiplexedNetworkConnection>>(),
                    provider.GetRequiredService<IClientTransport<ISimpleNetworkConnection>>()));

    private static IServiceCollection AddIceRpcServerTransport(this IServiceCollection services)
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
    private static IServiceCollection AddIceRpcClientTransport(this IServiceCollection services)
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
