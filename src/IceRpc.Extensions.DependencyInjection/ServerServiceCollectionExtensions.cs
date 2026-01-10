// Copyright (c) ZeroC, Inc.

using IceRpc.Extensions.DependencyInjection.Internal;
using IceRpc.Transports;
using IceRpc.Transports.Quic;
using IceRpc.Transports.Tcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Quic;

namespace IceRpc.Extensions.DependencyInjection;

/// <summary>Provides extension methods for <see cref="IServiceCollection" /> to add a <see cref="Server" />.</summary>
public static class ServerServiceCollectionExtensions
{
    /// <summary>Extension methods for <see cref="IServiceCollection" />.</summary>
    /// <param name="services">The service collection to add services to.</param>
    extension(IServiceCollection services)
    {
        /// <summary>Adds a <see cref="Server" /> with the specified dispatch pipeline to this service
        /// collection; you can specify the server's options by injecting an <see cref="IOptions{T}" /> of
        /// <see cref="ServerOptions" />.</summary>
        /// <param name="dispatcher">The dispatch pipeline.</param>
        /// <returns>The service collection.</returns>
        /// <example>
        /// The following code adds a Server singleton to the service collection.
        /// <code
        ///     source="../../docfx/examples/IceRpc.Extensions.DependencyInjection.Examples/AddIceRpcServerExamples.cs"
        ///     region="DefaultServer"
        ///     lang="csharp" />
        /// The resulting singleton is a default server: it uses the default server address and the default
        /// multiplexed transport (QUIC). If you want to customize this server, add an <see cref="IOptions{T}" /> of
        /// <see cref="ServerOptions" /> to your DI container:
        /// <code
        ///     source="../../docfx/examples/IceRpc.Extensions.DependencyInjection.Examples/AddIceRpcServerExamples.cs"
        ///     region="ServerWithOptions"
        ///     lang="csharp" />
        /// You can also inject a server transport:
        /// <list type="bullet">
        /// <item><description>an <see cref="IDuplexServerTransport" /> for the ice protocol</description>
        /// </item>
        /// <item><description>an <see cref="IMultiplexedServerTransport" /> for the icerpc protocol</description>
        /// </item>
        /// </list>
        ///
        /// For example, you can add a Slic over TCP server as follows:
        /// <code
        ///     source="../../docfx/examples/IceRpc.Extensions.DependencyInjection.Examples/AddIceRpcServerExamples.cs"
        ///     region="ServerWithSlic"
        ///     lang="csharp" />
        /// If you want to customize the options of the default transport (QUIC), you just need to inject
        /// an <see cref="IOptions{T}" /> of <see cref="QuicServerTransportOptions" />.
        /// </example>
        public IServiceCollection AddIceRpcServer(IDispatcher dispatcher) =>
            services.AddIceRpcServer(optionsName: Options.DefaultName, dispatcher);

        /// <summary>Adds a <see cref="Server" /> to this service collection and configures the dispatch pipeline of
        /// this server; you can specify the server's options by injecting an <see cref="IOptions{T}" /> of
        /// <see cref="ServerOptions" />.</summary>
        /// <param name="configure">The action to configure the dispatch pipeline using an
        /// <see cref="IDispatcherBuilder" />.</param>
        /// <returns>The service collection.</returns>
        /// <remarks>The dispatch pipeline built by this method is not registered in the DI container.</remarks>
        /// <example>
        /// The following code builds a dispatch pipeline and adds a server with this dispatch pipeline to the
        /// service collection.
        /// <code
        ///     source="../../docfx/examples/IceRpc.Extensions.DependencyInjection.Examples/AddIceRpcServerExamples.cs"
        ///     region="ServerWithDispatcherBuilder"
        ///     lang="csharp" />
        /// See also
        /// <see cref="ServerServiceCollectionExtensions.extension(IServiceCollection).AddIceRpcServer(IDispatcher)" />.
        /// </example>
        public IServiceCollection AddIceRpcServer(Action<IDispatcherBuilder> configure) =>
            services.AddIceRpcServer(optionsName: Options.DefaultName, configure);

        /// <summary>Adds a <see cref="Server" /> to this service collection; you specify the server's options by
        /// injecting an <see cref="IOptions{T}" /> of <see cref="ServerOptions" />.</summary>
        /// <returns>The service collection.</returns>
        /// <remarks>You need to set a least the dispatcher in the injected options.</remarks>
        public IServiceCollection AddIceRpcServer() =>
            services.AddIceRpcServer(Options.DefaultName);

        /// <summary>Adds a <see cref="Server" /> with the specified dispatch pipeline to this service collection;
        /// you can specify the server's options by injecting an <see cref="IOptionsMonitor{T}" /> of
        /// <see cref="ServerOptions" /> named <paramref name="optionsName" />.</summary>
        /// <param name="optionsName">The name of the options instance.</param>
        /// <param name="dispatcher">The dispatch pipeline of the server.</param>
        /// <returns>The service collection.</returns>
        /// <example>
        /// A server application may need to host multiple <see cref="Server" /> instances, each with its own options.
        /// A typical example is when you want to accept requests from clients over both the icerpc protocol and the
        /// ice protocol. This overload allows you to add two (or more) server singletons, each with its own options:
        /// <code
        ///     source="../../docfx/examples/IceRpc.Extensions.DependencyInjection.Examples/AddIceRpcServerExamples.cs"
        ///     region="ServerWithNamedOptions"
        ///     lang="csharp" />
        /// See also
        /// <see cref="ServerServiceCollectionExtensions.extension(IServiceCollection).AddIceRpcServer(IDispatcher)" />.
        /// </example>
        public IServiceCollection AddIceRpcServer(
            string optionsName,
            IDispatcher dispatcher)
        {
            services.AddOptions<ServerOptions>(optionsName).Configure(
                options => options.ConnectionOptions.Dispatcher = dispatcher);
            return services.AddIceRpcServer(optionsName);
        }

        /// <summary>Adds a <see cref="Server" /> to this service collection and configures the dispatch pipeline of
        /// this server; you can specify the server's options by injecting an <see cref="IOptionsMonitor{T}" /> of
        /// <see cref="ServerOptions" /> named <paramref name="optionsName" />.</summary>
        /// <param name="optionsName">The name of the options instance.</param>
        /// <param name="configure">The action to configure the dispatch pipeline using an
        /// <see cref="IDispatcherBuilder" />.</param>
        /// <returns>The service collection.</returns>
        /// <remarks>The dispatch pipeline built by this method is not registered in the DI container.</remarks>
        /// <seealso
        ///     cref="ServerServiceCollectionExtensions.extension(IServiceCollection).AddIceRpcServer(string, IDispatcher)" />
        public IServiceCollection AddIceRpcServer(
            string optionsName,
            Action<IDispatcherBuilder> configure) =>
            services
                .TryAddIceRpcServerTransport()
                .AddSingleton(provider =>
                {
                    var dispatcherBuilder = new DispatcherBuilder(provider);
                    configure(dispatcherBuilder);

                    ServerOptions options =
                        provider.GetRequiredService<IOptionsMonitor<ServerOptions>>().Get(optionsName);
                    options.ConnectionOptions.Dispatcher = dispatcherBuilder.Build();

                    return new Server(
                        options,
                        provider.GetRequiredService<IDuplexServerTransport>(),
                        provider.GetRequiredService<IMultiplexedServerTransport>(),
                        provider.GetService<ILogger<Server>>());
                });

        /// <summary>Adds a <see cref="Server" /> to this service collection; you specify the server's options by
        /// injecting an <see cref="IOptionsMonitor{T}" /> of <see cref="ServerOptions" /> named
        /// <paramref name="optionsName" />.</summary>
        /// <param name="optionsName">The name of the options instance.</param>
        /// <returns>The service collection.</returns>
        /// <remarks>You need to set a least the dispatcher in the injected options.</remarks>
        /// <seealso
        ///     cref="ServerServiceCollectionExtensions.extension(IServiceCollection).AddIceRpcServer(string, IDispatcher)" />
        public IServiceCollection AddIceRpcServer(string optionsName) =>
            services
                .TryAddIceRpcServerTransport()
                .AddSingleton(provider =>
                    new Server(
                        provider.GetRequiredService<IOptionsMonitor<ServerOptions>>().Get(optionsName),
                        provider.GetRequiredService<IDuplexServerTransport>(),
                        provider.GetRequiredService<IMultiplexedServerTransport>(),
                        provider.GetService<ILogger<Server>>()));

        private IServiceCollection TryAddIceRpcServerTransport()
        {
            // The default duplex transport is TCP.
            services
               .AddOptions()
               .TryAddSingleton<IDuplexServerTransport>(
                   provider => new TcpServerTransport(
                       provider.GetRequiredService<IOptions<TcpServerTransportOptions>>().Value));

            services
                .TryAddSingleton<IMultiplexedServerTransport>(
                    provider =>
                    {
                        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsWindows())
                        {
                            if (QuicListener.IsSupported)
                            {
                                return new QuicServerTransport(
                                    provider.GetRequiredService<IOptions<QuicServerTransportOptions>>().Value);
                            }
                            throw new NotSupportedException(
                                "The default QUIC server transport is not available on this system. " +
                                "Please review the Platform Dependencies for QUIC in the .NET documentation.");
                        }
                        throw new PlatformNotSupportedException(
                            "The default QUIC server transport is not supported on this platform. " +
                            "You need to register an IMultiplexedServerTransport implementation " +
                            "in the service collection.");
                    });

            return services;
        }
    }
}
