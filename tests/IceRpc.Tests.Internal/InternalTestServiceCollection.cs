// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net.Security;

namespace IceRpc.Tests.Internal
{
    internal class InternalTestServiceCollection : TransportServiceCollection
    {
        internal InternalTestServiceCollection()
        {
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
                        serviceProvider.GetRequiredService<ILogger>());

                LogNetworkConnectionDecoratorFactory<T>? decorator =
                    serviceProvider.GetService<LogNetworkConnectionDecoratorFactory<T>>();
                if (decorator != null)
                {
                    listener = new LogListenerDecorator<T>(listener, logger, decorator);
                }
                return listener;
            }
        }
    }

    public static class TransportServiceProviderExtensions
    {
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
                logger);
            LogNetworkConnectionDecoratorFactory<T>? decorator =
                serviceProvider.GetService<LogNetworkConnectionDecoratorFactory<T>>();
            if (decorator != null)
            {
                connection = decorator(connection, endpoint, false, logger);
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
