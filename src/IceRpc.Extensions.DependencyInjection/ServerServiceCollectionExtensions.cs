// Copyright (c) ZeroC, Inc.

using IceRpc.Extensions.DependencyInjection.Internal;
using IceRpc.Transports;
using IceRpc.Transports.Slic;
using IceRpc.Transports.Tcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IceRpc.Extensions.DependencyInjection;

/// <summary>Provides extension methods for adding a <see cref="Server" /> as a singleton service in an
/// <see cref="IServiceCollection" />.</summary>
public static class ServerServiceCollectionExtensions
{
    /// <summary>Adds a <see cref="Server" /> with the specified dispatcher to this service collection, as a singleton;
    /// you can specify the server's options by injecting an <see cref="IOptions{T}" /> of <see cref="ServerOptions" />.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="dispatcher">The server dispatcher.</param>
    /// <returns>The service collection.</returns>
    /// <example>
    /// The following code adds a Server singleton to the service collection.
    /// ```csharp
    /// IDispatcher dispatcher = ...; // the dispatch pipeline
    /// var builder = Host.CreateDefaultBuilder(args);
    /// builder.ConfigureServices(services => services.AddIceRpcServer(dispatcher));
    /// ```
    /// The resulting singleton is a default server: it uses the default server address, the default multiplexed
    /// transport (tcp) and <c>null</c> for its authentication options (so no TLS). If you want to add a more custom
    /// server, add an <see cref="IOptions{T}" /> of <see cref="ServerOptions" /> to your DI container. For example:
    /// ```csharp
    /// IDispatcher dispatcher = ...; // the dispatch pipeline
    /// var builder = Host.CreateDefaultBuilder(args);
    /// builder.UseContentRoot(AppContext.BaseDirectory).ConfigureServices((hostContext, services) =>
    /// {
    ///     services
    ///         .AddOptions&lt;ServerOptions>()
    ///         // Read the server options from configuration.
    ///         .Bind(hostContext.Configuration.GetSection("Server"));
    ///
    ///     services.AddIceRpcServer(dispatcher);
    /// }
    /// ```
    /// You can also inject a server transport--a <see cref="IMultiplexedServerTransport" /> for the icerpc protocol,
    /// or a <see cref="IDuplexServerTransport" /> for the ice protocol.
    /// ```csharp
    /// using IceRpc.Transports.Quic;
    ///
    /// IDispatcher dispatcher = ...; // the dispatch pipeline
    /// var builder = Host.CreateDefaultBuilder(args)
    /// builder.ConfigureServices(services =>
    ///     // Inject an IMultiplexedServerTransport singleton implemented by QUIC.
    ///     services
    ///         .AddSingleton&lt;IMultiplexedServerTransport>(provider => new QuicServerTransport())
    ///         .AddIceRpcServer(dispatcher));
    /// ```
    /// If you want to keep the default transport (tcp) but want to customize its options, you just need to inject
    /// an <see cref="IOptions{T}" /> of <see cref="TcpServerTransportOptions" />.
    /// </example>
    public static IServiceCollection AddIceRpcServer(this IServiceCollection services, IDispatcher dispatcher) =>
        services.AddIceRpcServer(optionsName: Options.DefaultName, dispatcher);

    /// <summary>Adds a <see cref="Server" /> to this service collection and build a dispatch pipeline for this server;
    /// you can specify the server's options by injecting an <see cref="IOptions{T}" /> of <see cref="ServerOptions" />.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="configure">The action to configure the dispatch pipeline using an
    /// <see cref="IDispatcherBuilder" />.</param>
    /// <returns>The service collection.</returns>
    /// <remarks>The dispatch pipeline built by this method is not registered in the DI container.</remarks>
    /// <example>
    /// The following code builds a dispatch pipeline and adds a server to the service collection with this dispatch
    /// pipeline.
    /// ```csharp
    /// var hostBuilder = Host.CreateDefaultBuilder(args);
    /// hostBuilder.ConfigureServices(services =>
    ///     services
    ///        .AddIceRpcServer(builder =>
    ///            builder
    ///                .UseTelemetry()
    ///                .UseLogger()
    ///                .Map&lt;IGreeterService>());
    /// ```
    /// See also <see cref="AddIceRpcServer(IServiceCollection, IDispatcher)" />.
    /// </example>
    public static IServiceCollection AddIceRpcServer(
        this IServiceCollection services,
        Action<IDispatcherBuilder> configure) =>
        services.AddIceRpcServer(optionsName: Options.DefaultName, configure);

    /// <summary>Adds a <see cref="Server" /> to this service collection; its dispatch pipeline is the
    /// <see cref="IDispatcher" /> provided by the DI container and you can specify the server's options by injecting an
    /// <see cref="IOptions{T}" /> of <see cref="ServerOptions" />.</summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <returns>The service collection.</returns>
    /// <remarks>You can build a dispatch pipeline singleton with
    /// <see cref="IceRpcServiceCollectionExtensions.AddIceRpcDispatcher" />.</remarks>
    /// <seealso cref="AddIceRpcServer(IServiceCollection, IDispatcher)" />
    public static IServiceCollection AddIceRpcServer(this IServiceCollection services) =>
        services.AddIceRpcServer(Options.DefaultName);

    /// <summary>Adds a <see cref="Server" /> with the specified dispatcher to this service collection, as a singleton;
    /// you can specify the server's options by injecting an <see cref="IOptionsMonitor{T}" /> of
    /// <see cref="ServerOptions" /> with name <paramref name="optionsName" />.</summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="optionsName">The name of the options instance.</param>
    /// <param name="dispatcher">The dispatch pipeline of the server.</param>
    /// <returns>The service collection.</returns>
    /// <example>
    /// A server application many need to host multiple <see cref="Server" /> instances, each with its own options. A
    /// typical example if when you want to process requests for clients over both the icerpc protocol and the ice
    /// protocol. This overload allows you create two (or more) server singletons, each with its own options:
    /// ```csharp
    /// IDispatcher dispatcher = ...; // the dispatch pipeline
    /// var builder = Host.CreateDefaultBuilder(args);
    /// builder.ConfigureServices(services =>
    /// {
    ///     // The server options for the icerpc server
    ///     services
    ///         .AddOptions&lt;ServerOptions>("IceRpcGreeter") // named option
    ///         .Bind(hostContext.Configuration.GetSection("IceRpcGreeter"));
    ///
    ///     // The server options for the ice server
    ///     services
    ///       .AddOptions&lt;ServerOptions>("IceGreeter")
    ///       .Bind(hostContext.Configuration.GetSection("IceGreeter"));
    ///
    ///     // We pass the named server options to get the correct server options for each server.
    ///     services.AddIceRpcServer("IceRpcGreeter", dispatcher);
    ///     services.AddIceRpcServer("IceGreeter", dispatcher));
    /// });
    /// ```
    /// See also <see cref="AddIceRpcServer(IServiceCollection, IDispatcher)" />.
    /// </example>
    public static IServiceCollection AddIceRpcServer(
        this IServiceCollection services,
        string optionsName,
        IDispatcher dispatcher)
    {
        services.AddOptions<ServerOptions>(optionsName).Configure(
            options => options.ConnectionOptions.Dispatcher = dispatcher);
        return services.AddIceRpcServer(optionsName);
    }

    /// <summary>Adds a <see cref="Server" /> with the specified name to this service collection. </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="optionsName">The name of the ServerOptions instance.</param>
    /// <param name="configure">The action to configure the dispatcher using a <see cref="DispatcherBuilder" />.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddIceRpcServer(
        this IServiceCollection services,
        string optionsName,
        Action<IDispatcherBuilder> configure) =>
        services
            .TryAddIceRpcServerTransport()
            .AddSingleton(provider =>
            {
                var dispatcherBuilder = new DispatcherBuilder(provider);
                configure(dispatcherBuilder);

                ServerOptions options = provider.GetRequiredService<IOptionsMonitor<ServerOptions>>().Get(optionsName);
                options.ConnectionOptions.Dispatcher = dispatcherBuilder.Build();

                return new Server(
                    options,
                    provider.GetRequiredService<IDuplexServerTransport>(),
                    provider.GetRequiredService<IMultiplexedServerTransport>(),
                    provider.GetService<ILogger<Server>>());
            });

    /// <summary>Adds a <see cref="Server" /> to this service collection.</summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="optionsName">The name of the ServerOptions instance.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddIceRpcServer(this IServiceCollection services, string optionsName) =>
        services
            .TryAddIceRpcServerTransport()
            .AddSingleton(provider =>
                new Server(
                    provider.GetRequiredService<IOptionsMonitor<ServerOptions>>().Get(optionsName),
                    provider.GetRequiredService<IDuplexServerTransport>(),
                    provider.GetRequiredService<IMultiplexedServerTransport>(),
                    provider.GetService<ILogger<Server>>()));

    private static IServiceCollection TryAddIceRpcServerTransport(this IServiceCollection services)
    {
        services
           .AddOptions()
           .TryAddSingleton<IDuplexServerTransport>(
               provider => new TcpServerTransport(
                   provider.GetRequiredService<IOptions<TcpServerTransportOptions>>().Value));

        services
            .TryAddSingleton<IMultiplexedServerTransport>(
                provider => new SlicServerTransport(
                    provider.GetRequiredService<IOptions<SlicTransportOptions>>().Value,
                    provider.GetRequiredService<IDuplexServerTransport>()));

        return services;
    }
}
