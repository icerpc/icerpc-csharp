// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Slice;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net.Security;

namespace IceRpc.Tests
{
    public class IntegrationTestServiceCollection : TransportServiceCollection
    {
        public IntegrationTestServiceCollection()
        {
            // The default Server instance.
            this.AddScoped(serviceProvider =>
            {
                IServerTransport<ISimpleNetworkConnection> simpleServerTransport =
                    serviceProvider.GetRequiredService<IServerTransport<ISimpleNetworkConnection>>();
                IServerTransport<IMultiplexedNetworkConnection> multiplexedServerTransport =
                    serviceProvider.GetRequiredService<IServerTransport<IMultiplexedNetworkConnection>>();

                var server = new Server(new ServerOptions
                {
                    AuthenticationOptions = serviceProvider.GetService<SslServerAuthenticationOptions>(),
                    Dispatcher = serviceProvider.GetService<IDispatcher>() ?? ConnectionOptions.DefaultDispatcher,
                    Endpoint = serviceProvider.GetRequiredService<Endpoint>(),
                    LoggerFactory = serviceProvider.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance,
                    MultiplexedServerTransport = multiplexedServerTransport,
                    SimpleServerTransport = simpleServerTransport,
                    // Use 60s timeout otherwise tests causing connection shutdown issues might silently succeed.
                    CloseTimeout = TimeSpan.FromSeconds(60),
                    IceProtocolOptions = serviceProvider.GetService<IceProtocolOptions>()
                });

                server.Listen();
                return server;
            });

            // The default ConnectionOptions is configured to connect to the Server configured on the service
            // collection.
            this.AddScoped(serviceProvider =>
            {
                IClientTransport<ISimpleNetworkConnection> simpleClientTransport =
                    serviceProvider.GetRequiredService<IClientTransport<ISimpleNetworkConnection>>();

                IClientTransport<IMultiplexedNetworkConnection> multiplexedClientTransport =
                    serviceProvider.GetRequiredService<IClientTransport<IMultiplexedNetworkConnection>>();

                return new ConnectionOptions
                {
                    AuthenticationOptions = serviceProvider.GetService<SslClientAuthenticationOptions>(),
                    RemoteEndpoint = serviceProvider.GetRequiredService<Server>().Endpoint,
                    SimpleClientTransport = simpleClientTransport,
                    MultiplexedClientTransport = multiplexedClientTransport,
                    LoggerFactory = serviceProvider.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance,
                    IsResumable = serviceProvider.GetService<ResumableConnection>() != null,
                    // Use 60s timeout otherwise tests causing connection shutdown issues might silently succeed.
                    CloseTimeout = TimeSpan.FromSeconds(60),
                    IceProtocolOptions = serviceProvider.GetService<IceProtocolOptions>()
                };
            });

            // The default Connection is configured to connect to the Server configured on the service collection.
            this.AddScoped(serviceProvider => new Connection(serviceProvider.GetRequiredService<ConnectionOptions>()));

            // The default ConnectionPool is configured with the client transport from the service collection.
            this.AddScoped(
                serviceProvider => new ConnectionPool(serviceProvider.GetRequiredService<ConnectionOptions>()));

            // The default proxy is created from the Connection configured on the service collection.
            this.AddScoped(serviceProvider => Proxy.FromConnection(
                serviceProvider.GetRequiredService<Connection>(),
                path: "/test",
                invoker: serviceProvider.GetService<IInvoker>()));
        }

        internal class ResumableConnection
        {
        }
    }

    public static class IntegrationTestServiceCollectionExtensions
    {
        public static IServiceCollection UseProtocol(this IServiceCollection collection, string protocol) =>
            collection.AddScoped(_ => Protocol.FromString(protocol));

        public static IServiceCollection UseVoidDispatcher(this IServiceCollection collection) =>
            collection.AddTransient<IDispatcher>(_ => new InlineDispatcher((request, cancel) =>
                new(new OutgoingResponse(request))));

        public static IServiceCollection UseResumableConnection(this IServiceCollection collection) =>
            collection.AddTransient(_ => new IntegrationTestServiceCollection.ResumableConnection());
    }

    public static class IntegrationTestServiceProviderExtensions
    {
        public static T GetProxy<T>(this IServiceProvider serviceProvider, string? path = null) where T : IPrx, new() =>
            new()
            {
                Proxy = serviceProvider.GetRequiredService<Proxy>() with { Path = path ?? typeof(T).GetDefaultPath() }
            };
    }
}
