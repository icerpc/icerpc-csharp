// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net.Security;

namespace IceRpc.Tests
{
    public class NetworkConnectionTestServiceCollection : ServiceCollection
    {
        public NetworkConnectionTestServiceCollection()
        {
            this.AddScoped<ColocTransport>();

            this.AddScoped<ILoggerFactory>(_ => LogAttributeLoggerFactory.Instance);

            this.AddScoped(serviceProvider =>
                serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Test"));

            this.AddScoped(serviceProvider =>
                new TcpServerOptions
                {
                    AuthenticationOptions = serviceProvider.GetService<SslServerAuthenticationOptions>()
                });

            this.AddScoped(serviceProvider =>
                new TcpClientOptions
                {
                    AuthenticationOptions = serviceProvider.GetService<SslClientAuthenticationOptions>()
                });

            // Default protocol is Ice2
            this.AddScoped(_ => Protocol.Ice2);

            // The default server endpoint is the server coloc transport default endpoint.
            this.AddScoped(serviceProvider =>
                serviceProvider.GetRequiredService<ColocTransport>().ServerTransport.DefaultEndpoint);

            // The default simple server transport is based on the transport of the service collection endpoint.
            this.AddScoped(serviceProvider =>
                serviceProvider.GetRequiredService<Endpoint>().Transport switch
                {
                    "tcp" => new TcpServerTransport(serviceProvider.GetService<TcpServerOptions>() ?? new()),
                    "ssl" => new TcpServerTransport(serviceProvider.GetService<TcpServerOptions>() ?? new()),
                    "udp" => new UdpServerTransport(serviceProvider.GetService<UdpServerOptions>() ?? new()),
                    "coloc" => serviceProvider.GetRequiredService<ColocTransport>().ServerTransport,
                    _ => Server.DefaultSimpleServerTransport
                });

            // The default multiplexed server transport is Slic.
            this.AddScoped<IServerTransport<IMultiplexedNetworkConnection>>(serviceProvider =>
                serviceProvider.GetRequiredService<Endpoint>().Transport switch
                {
                    "udp" => new CompositeMultiplexedServerTransport(),
                    _ => new SlicServerTransport(
                        serviceProvider.GetRequiredService<IServerTransport<ISimpleNetworkConnection>>(),
                        serviceProvider.GetService<SlicOptions>() ?? new())
                });

            // The default simple client transport is based on the transport of the service collection endpoint.
            this.AddScoped(serviceProvider =>
                serviceProvider.GetRequiredService<Endpoint>().Transport switch
                {
                    "tcp" => new TcpClientTransport(serviceProvider.GetService<TcpClientOptions>() ?? new()),
                    "ssl" => new TcpClientTransport(serviceProvider.GetService<TcpClientOptions>() ?? new()),
                    "udp" => new UdpClientTransport(serviceProvider.GetService<UdpClientOptions>() ?? new()),
                    "coloc" => serviceProvider.GetRequiredService<ColocTransport>().ClientTransport,
                    _ => Connection.DefaultSimpleClientTransport
                });

            // The default multiplexed client transport is Slic.
            this.AddScoped<IClientTransport<IMultiplexedNetworkConnection>>(serviceProvider =>
                serviceProvider.GetRequiredService<Endpoint>().Transport switch
                {
                    "udp" => new CompositeMultiplexedClientTransport(),
                    _ => new SlicClientTransport(
                        serviceProvider.GetRequiredService<IClientTransport<ISimpleNetworkConnection>>(),
                        serviceProvider.GetService<SlicOptions>() ?? new())
                });

            // The simple server transport listener
            this.AddScoped(serviceProvider =>
                serviceProvider.GetRequiredService<IServerTransport<ISimpleNetworkConnection>>().Listen(
                    serviceProvider.GetRequiredService<Endpoint>(),
                    serviceProvider.GetRequiredService<ILogger>()));

            // The multiplexed server transport listener
            this.AddScoped(serviceProvider =>
                serviceProvider.GetRequiredService<IServerTransport<IMultiplexedNetworkConnection>>().Listen(
                    serviceProvider.GetRequiredService<Endpoint>(),
                    serviceProvider.GetRequiredService<ILogger>()));
        }
    }

    public static class NetworkConnectionTestServiceProviderExtensions
    {
        public static Task<IMultiplexedNetworkConnection> GetMultiplexedClientConnectionAsync(
            this IServiceProvider serviceProvider) =>
            GetClientConnectionAsync<IMultiplexedNetworkConnection>(serviceProvider);

        public static Task<IMultiplexedNetworkConnection> GetMultiplexedServerConnectionAsync(
            this IServiceProvider serviceProvider) =>
            GetServerConnectionAsync<IMultiplexedNetworkConnection>(serviceProvider);

        public static Task<ISimpleNetworkConnection> GetSimpleClientConnectionAsync(
            this IServiceProvider serviceProvider) =>
            GetClientConnectionAsync<ISimpleNetworkConnection>(serviceProvider);

        public static Task<ISimpleNetworkConnection> GetSimpleServerConnectionAsync(
            this IServiceProvider serviceProvider) =>
            GetServerConnectionAsync<ISimpleNetworkConnection>(serviceProvider);

        private static async Task<T> GetClientConnectionAsync<T>(
            IServiceProvider serviceProvider) where T : INetworkConnection
        {
            Endpoint endpoint = serviceProvider.GetRequiredService<IListener<T>>().Endpoint;
            T connection = serviceProvider.GetRequiredService<IClientTransport<T>>().CreateConnection(
                endpoint,
                serviceProvider.GetRequiredService<ILogger>());
            await connection.ConnectAsync(default);
            return connection;
        }

        private static async Task<T> GetServerConnectionAsync<T>(
            IServiceProvider serviceProvider) where T : INetworkConnection
        {
            T connection = await serviceProvider.GetRequiredService<IListener<T>>().AcceptAsync();
            await connection.ConnectAsync(default);
            return connection;
        }
    }
}
