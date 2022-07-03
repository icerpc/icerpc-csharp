// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc;
using IceRpc.Builder;
using IceRpc.Builder.Internal;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Extension methods for setting up IceRpc services in an <see cref="IServiceCollection"/>.</summary>
public static class IceRpcServiceCollectionExtensions
{
    /// <summary>Adds a <see cref="ClientConnection"/>, <see cref="IClientConnection"/> and <see cref="IInvoker"/>
    /// singleton to this service collection.</summary>
    /// <param name="services">The service collection to add services to.</param>
    public static IServiceCollection AddIceRpcClientConnection(this IServiceCollection services) =>
        services
            .TryAddIceRpcClientTransport()
            .AddSingleton(provider =>
                new ClientConnection(
                    provider.GetRequiredService<IOptions<ClientConnectionOptions>>().Value,
                    loggerFactory: provider.GetService<ILoggerFactory>(),
                    provider.GetRequiredService<IClientTransport<IMultiplexedNetworkConnection>>(),
                    provider.GetRequiredService<IClientTransport<ISimpleNetworkConnection>>()))
            .AddSingleton<IClientConnection>(provider => provider.GetRequiredService<ClientConnection>())
            .AddSingleton<IInvoker>(provider => provider.GetRequiredService<ClientConnection>());

    /// <summary>Adds a <see cref="ConnectionPool"/> and <see cref="IInvoker"/> singleton to this service collection.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    public static IServiceCollection AddIceRpcConnectionPool(this IServiceCollection services) =>
        services
            .TryAddIceRpcClientTransport()
            .AddSingleton(provider =>
                new ConnectionPool(
                    provider.GetRequiredService<IOptions<ConnectionPoolOptions>>().Value,
                    loggerFactory: provider.GetService<ILoggerFactory>(),
                    provider.GetRequiredService<IClientTransport<IMultiplexedNetworkConnection>>(),
                    provider.GetRequiredService<IClientTransport<ISimpleNetworkConnection>>()))
            .AddSingleton<IInvoker>(provider => provider.GetRequiredService<ConnectionPool>());

    /// <summary>Adds an <see cref="IDispatcher"/> singleton to this service collection using a builder.</summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="configure">The action to configure the dispatcher builder.</param>
    public static IServiceCollection AddIceRpcDispatcher(
        this IServiceCollection services,
        Action<IDispatcherBuilder> configure) =>
        services
            .AddSingleton<IDispatcher>(provider =>
            {
                var builder = new DispatcherBuilder(provider);
                configure(builder);
                return builder.Build();
            });

    /// <summary>Adds an <see cref="IInvoker"/> singleton to this service collection using a builder.</summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="configure">The action to configure the invoker builder.</param>
    public static IServiceCollection AddIceRpcInvoker(
        this IServiceCollection services,
        Action<IInvokerBuilder> configure) =>
        services
            .AddSingleton<IInvoker>(provider =>
            {
                var builder = new InvokerBuilder(provider);
                configure(builder);
                return builder.Build();
            });

    /// <summary>Adds a <see cref="ResumableClientConnection"/>, <see cref="IClientConnection"/> and
    /// <see cref="IInvoker"/> singleton to this service collection.</summary>
    /// <param name="services">The service collection to add services to.</param>
    public static IServiceCollection AddIceRpcResumableClientConnection(this IServiceCollection services) =>
        services
            .TryAddIceRpcClientTransport()
            .AddSingleton<ResumableClientConnection>(provider =>
                new ResumableClientConnection(
                    provider.GetRequiredService<IOptions<ClientConnectionOptions>>().Value,
                    loggerFactory: provider.GetService<ILoggerFactory>(),
                    provider.GetRequiredService<IClientTransport<IMultiplexedNetworkConnection>>(),
                    provider.GetRequiredService<IClientTransport<ISimpleNetworkConnection>>()))
            .AddSingleton<IClientConnection>(provider => provider.GetRequiredService<ResumableClientConnection>())
            .AddSingleton<IInvoker>(provider => provider.GetRequiredService<ResumableClientConnection>());

    /// <summary>Adds a <see cref="Server"/> to this service collection.</summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="optionsName">The name of the ServerOptions instance.</param>
    public static IServiceCollection AddIceRpcServer(this IServiceCollection services, string optionsName) =>
        services
            .TryAddIceRpcServerTransport()
            .AddSingleton<Server>(provider =>
                new Server(
                    provider.GetRequiredService<IOptionsMonitor<ServerOptions>>().Get(optionsName),
                    loggerFactory: provider.GetService<ILoggerFactory>(),
                    provider.GetRequiredService<IServerTransport<IMultiplexedNetworkConnection>>(),
                    provider.GetRequiredService<IServerTransport<ISimpleNetworkConnection>>()));

    /// <summary>Adds a <see cref="Server"/> to this service collection. This method uses the default name ("") for the
    /// ServerOptions instance.</summary>
    public static IServiceCollection AddIceRpcServer(this IServiceCollection services) =>
        services.AddIceRpcServer(Options.Options.DefaultName);

    /// <summary>Adds a <see cref="Server"/> with the specified dispatcher to this service collection.</summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="optionsName">The name of the ServerOptions instance.</param>
    /// <param name="dispatcher">The server dispatcher.</param>
    public static IServiceCollection AddIceRpcServer(
        this IServiceCollection services,
        string optionsName,
        IDispatcher dispatcher)
    {
        services.AddOptions<ServerOptions>(optionsName).Configure(
            options => options.ConnectionOptions.Dispatcher = dispatcher);
        return services.AddIceRpcServer(optionsName);
    }

    /// <summary>Adds a <see cref="Server"/> with the specified dispatcher to this service collection. This method uses
    /// the default name ("") for the ServerOptions instance.</summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="dispatcher">The server dispatcher.</param>
    public static IServiceCollection AddIceRpcServer(this IServiceCollection services, IDispatcher dispatcher) =>
        services.AddIceRpcServer(optionsName: Options.Options.DefaultName, dispatcher);

    /// <summary>Adds a <see cref="Server"/> with the specified name to this service collection. </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="optionsName">The server name.</param>
    /// <param name="configure">The action to configure the dispatcher using a <see cref="DispatcherBuilder"/>.</param>
    public static IServiceCollection AddIceRpcServer(
        this IServiceCollection services,
        string optionsName,
        Action<IDispatcherBuilder> configure) =>
        services
            .TryAddIceRpcServerTransport()
            .AddSingleton<Server>(provider =>
            {
                var dispatcherBuilder = new DispatcherBuilder(provider);
                configure(dispatcherBuilder);

                ServerOptions options = provider.GetRequiredService<IOptionsMonitor<ServerOptions>>().Get(optionsName);
                options.ConnectionOptions.Dispatcher = dispatcherBuilder.Build();

                return new Server(
                    options,
                    loggerFactory: provider.GetService<ILoggerFactory>(),
                    provider.GetRequiredService<IServerTransport<IMultiplexedNetworkConnection>>(),
                    provider.GetRequiredService<IServerTransport<ISimpleNetworkConnection>>());
            });

    /// <summary>Adds a <see cref="Server"/> to this service collection. This method uses the default name ("") for the
    /// ServerOptions instance.</summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="configure">The action to configure the dispatcher using a <see cref="DispatcherBuilder"/>.</param>
    public static IServiceCollection AddIceRpcServer(
        this IServiceCollection services,
        Action<IDispatcherBuilder> configure) =>
        services.AddIceRpcServer(optionsName: Options.Options.DefaultName, configure);

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
