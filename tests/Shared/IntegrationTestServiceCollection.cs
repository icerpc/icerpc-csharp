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

                var server = new Server
                {
                    Dispatcher = serviceProvider.GetService<IDispatcher>(),
                    Endpoint = serviceProvider.GetRequiredService<Endpoint>(),
                    SimpleServerTransport = simpleServerTransport,
                    MultiplexedServerTransport = multiplexedServerTransport,
                    ConnectionOptions = serviceProvider.GetService<ConnectionOptions>() ?? new(),
                    LoggerFactory = serviceProvider.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance
                };
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
                    LoggerFactory = serviceProvider.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance
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
                path: "/",
                invoker: serviceProvider.GetService<IInvoker>()));
        }
    }

    public static class IntegrationTestServiceProviderExtensions
    {
        public static T GetProxy<T>(this IServiceProvider serviceProvider, string? path = null) where T : IPrx, new()
        {
            Proxy proxy = serviceProvider.GetRequiredService<Proxy>().Clone();
            return new T { Proxy = proxy.WithPath(path ?? typeof(T).GetDefaultPath()) };
        }

        public static IServiceCollection UseProtocol(this IServiceCollection collection, ProtocolCode protocolCode) =>
            collection.AddScoped(_ => Protocol.FromProtocolCode(protocolCode));

        public static IServiceCollection UseVoidDispatcher(this IServiceCollection collection) =>
            collection.AddTransient<IDispatcher>(_ => new InlineDispatcher((request, cancel) =>
                new(OutgoingResponse.ForPayload(request, ReadOnlyMemory<ReadOnlyMemory<byte>>.Empty))));
    }
}
