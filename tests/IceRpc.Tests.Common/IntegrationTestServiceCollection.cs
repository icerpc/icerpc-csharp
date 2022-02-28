// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Slice;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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

                ConnectionOptions connectionOptions = serviceProvider.GetService<ConnectionOptions>() ??
                    new ConnectionOptions();

                var server = new Server(new ServerOptions
                {
                    CloseTimeout = connectionOptions.CloseTimeout,
                    ConnectTimeout = connectionOptions.ConnectTimeout,
                    Dispatcher = serviceProvider.GetService<IDispatcher>() ?? Connection.DefaultDispatcher,
                    Endpoint = serviceProvider.GetRequiredService<Endpoint>(),
                    IncomingFrameMaxSize = connectionOptions.IncomingFrameMaxSize,
                    KeepAlive = connectionOptions.KeepAlive,
                    LoggerFactory = serviceProvider.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance,
                    MultiplexedServerTransport = multiplexedServerTransport,
                    SimpleServerTransport = simpleServerTransport,
                });

                server.Listen();
                return server;
            });

            // The default Connection is configured to connect to the Server configured on the service collection.
            this.AddScoped(serviceProvider =>
            {
                IClientTransport<ISimpleNetworkConnection> simpleClientTransport =
                    serviceProvider.GetRequiredService<IClientTransport<ISimpleNetworkConnection>>();

                IClientTransport<IMultiplexedNetworkConnection> multiplexedClientTransport =
                    serviceProvider.GetRequiredService<IClientTransport<IMultiplexedNetworkConnection>>();

                return new Connection
                {
                    RemoteEndpoint = serviceProvider.GetRequiredService<Server>().Endpoint,
                    SimpleClientTransport = simpleClientTransport,
                    MultiplexedClientTransport = multiplexedClientTransport,
                    Options = serviceProvider.GetService<ConnectionOptions>() ?? new(),
                    LoggerFactory = serviceProvider.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance,
                    IsResumable = serviceProvider.GetService<ResumableConnection>() != null
                };
            });

            // The default ConnectionPool is configured with the client transport from the service collection.
            this.AddScoped(serviceProvider =>
            {
                IClientTransport<ISimpleNetworkConnection> simpleClientTransport =
                    serviceProvider.GetRequiredService<IClientTransport<ISimpleNetworkConnection>>();

                IClientTransport<IMultiplexedNetworkConnection> multiplexedClientTransport =
                    serviceProvider.GetRequiredService<IClientTransport<IMultiplexedNetworkConnection>>();

                return new ConnectionPool
                {
                    SimpleClientTransport = simpleClientTransport,
                    MultiplexedClientTransport = multiplexedClientTransport,
                    ConnectionOptions = serviceProvider.GetService<ConnectionOptions>() ?? new(),
                    LoggerFactory = serviceProvider.GetService<ILoggerFactory>() ?? LogAttributeLoggerFactory.Instance
                };
            });

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
