// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Transports;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net.Security;

namespace IceRpc.Tests
{
    public class TransportServiceCollection : ServiceCollection
    {
        public TransportServiceCollection()
        {
            this.AddScoped<ColocTransport>();

            this.AddScoped<ILoggerFactory>(_ => LogAttributeLoggerFactory.Instance);

            this.AddScoped(serviceProvider =>
                serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Test"));

            this.AddScoped(serviceProvider => new TcpServerTransportOptions());

            // The default protocol is IceRpc
            this.AddScoped(_ => Protocol.IceRpc);

            // Use coloc as the default transport.
            this.UseTransport("coloc");

            // The default simple server transport is based on the configured endpoint.
            this.AddScoped(serviceProvider =>
                GetTransport(serviceProvider.GetRequiredService<Endpoint>()) switch
                {
                    "tcp" => new TcpServerTransport(serviceProvider.GetService<TcpServerTransportOptions>() ?? new()),
                    "ssl" => new TcpServerTransport(serviceProvider.GetService<TcpServerTransportOptions>() ?? new()),
                    "coloc" => serviceProvider.GetRequiredService<ColocTransport>().ServerTransport,
                    _ => ServerOptions.DefaultSimpleServerTransport
                });

            this.AddScoped(serviceProvider =>
                new SlicServerTransportOptions
                {
                   SimpleServerTransport =
                      serviceProvider.GetRequiredService<IServerTransport<ISimpleNetworkConnection>>()
                });

            // The default multiplexed server transport is Slic.
            this.AddScoped<IServerTransport<IMultiplexedNetworkConnection>>(serviceProvider =>
            {
                var serverTransportOptions = serviceProvider.GetRequiredService<SlicServerTransportOptions>();
                return serverTransportOptions.SimpleServerTransport == null ?
                    new CompositeMultiplexedServerTransport() :
                    new SlicServerTransport(serverTransportOptions);
            });

            // The default simple client transport is based on the configured endpoint.
            this.AddScoped(serviceProvider =>
                GetTransport(serviceProvider.GetRequiredService<Endpoint>()) switch
                {
                    "tcp" => new TcpClientTransport(serviceProvider.GetService<TcpClientTransportOptions>() ?? new()),
                    "ssl" => new TcpClientTransport(serviceProvider.GetService<TcpClientTransportOptions>() ?? new()),
                    "coloc" => serviceProvider.GetRequiredService<ColocTransport>().ClientTransport,
                    _ => ConnectionOptions.DefaultSimpleClientTransport
                });

            this.AddScoped(serviceProvider =>
                new SlicClientTransportOptions
                {
                    SimpleClientTransport =
                        serviceProvider.GetRequiredService<IClientTransport<ISimpleNetworkConnection>>()
                });

            // The default multiplexed client transport is Slic.
            this.AddScoped<IClientTransport<IMultiplexedNetworkConnection>>(serviceProvider =>
            {
                var clientTransportOptions = serviceProvider.GetRequiredService<SlicClientTransportOptions>();
                return clientTransportOptions.SimpleClientTransport == null ?
                    new CompositeMultiplexedClientTransport() :
                    new SlicClientTransport(clientTransportOptions);
            });

            // TODO: would be nicer to have a null default
            static string? GetTransport(Endpoint endpoint) =>
                endpoint.Params.TryGetValue("transport", out string? value) ? value : "tcp";

            // Add internal transport log decorators to enable logging for internal tests.
            this.AddScoped<LogNetworkConnectionDecoratorFactory<ISimpleNetworkConnection>>(
                _ => LogSimpleNetworkConnectionDecorator.Decorate);
            this.AddScoped<LogNetworkConnectionDecoratorFactory<IMultiplexedNetworkConnection>>(
                _ => LogMultiplexedNetworkConnectionDecorator.Decorate);

            this.AddScoped(serviceProvider => CreateListener<ISimpleNetworkConnection>(serviceProvider));
            this.AddScoped(serviceProvider => CreateListener<IMultiplexedNetworkConnection>(serviceProvider));

            static IListener<T> CreateListener<T>(IServiceProvider serviceProvider) where T : INetworkConnection
            {
                ILogger logger = serviceProvider.GetRequiredService<ILogger>();

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

    public static class TransportServiceCollectionExtensions
    {
        public static IServiceCollection UseColoc(
            this IServiceCollection collection,
            ColocTransport transport,
            int port) =>
            collection.AddScoped(_ => transport).UseEndpoint("coloc", host: "coloctest", port);

        public static IServiceCollection UseTransport(this IServiceCollection collection, string transport) =>
            collection.UseEndpoint(transport, "[::1]", 0);

        public static IServiceCollection UseEndpoint(
            this IServiceCollection collection,
            string transport,
            string host,
            int port)
        {
            collection.AddScoped(
                typeof(Endpoint),
                serviceProvider =>
                {
                    Protocol protocol = serviceProvider.GetRequiredService<Protocol>();
                    return Endpoint.FromString($"{protocol}://{host}:{port}?transport={transport}");
                });

            return collection;
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
            ILogger logger = serviceProvider.GetRequiredService<ILogger>();
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
}
