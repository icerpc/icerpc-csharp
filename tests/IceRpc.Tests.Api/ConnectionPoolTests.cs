// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Collections.Immutable;
using System.Net.Security;

namespace IceRpc.Tests.Api
{
    [Timeout(5000)]
    public class ConnectionPoolTests
    {
        private readonly ServiceProvider _serviceProvider;
        private readonly IClientTransport<IMultiplexedNetworkConnection> _clientTransport;
        private readonly Endpoint _remoteEndpoint;

        public ConnectionPoolTests()
        {
            _serviceProvider = new IntegrationTestServiceCollection().BuildServiceProvider();
            _clientTransport = _serviceProvider.GetRequiredService<IClientTransport<IMultiplexedNetworkConnection>>();
            _remoteEndpoint = _serviceProvider.GetRequiredService<Server>().Endpoint;
        }

        [OneTimeTearDown]
        public ValueTask DisposeAsync() => _serviceProvider.DisposeAsync();

        [Test]
        public async Task ConnectionPool_Dispose()
        {
            {
                var connectionPool = new ConnectionPool();
                await connectionPool.DisposeAsync();
                await connectionPool.DisposeAsync();
            }

            {
                var connectionPool = new ConnectionPool(
                    new ConnectionOptions { MultiplexedClientTransport = _clientTransport });
                _ = await connectionPool.GetConnectionAsync(
                    _remoteEndpoint,
                    ImmutableList<Endpoint>.Empty,
                    default);
                await connectionPool.DisposeAsync();
                await connectionPool.DisposeAsync();
            }
        }

        [Test]
        public async Task ConnectionPool_ConnectionOptions()
        {
            await using var connectionPool = new ConnectionPool(
                new ConnectionOptions
                {
                    IncomingFrameMaxSize = 2048,
                    KeepAlive = true,
                    MultiplexedClientTransport = _clientTransport
                });

            Connection connection = await connectionPool.GetConnectionAsync(
                _remoteEndpoint,
                ImmutableList<Endpoint>.Empty,
                default);
        }

        [Test]
        public async Task ConnectionPool_ConnectionReused()
        {
            await using var connectionPool = new ConnectionPool(
                new ConnectionOptions { MultiplexedClientTransport = _clientTransport });

            Connection connection = await connectionPool.GetConnectionAsync(
                _remoteEndpoint,
                ImmutableList<Endpoint>.Empty,
                default);

            Connection connection1 = await connectionPool.GetConnectionAsync(
                _remoteEndpoint,
                ImmutableList<Endpoint>.Empty,
                default);

            Connection connection2 = await connectionPool.GetConnectionAsync(
                _remoteEndpoint,
                ImmutableList<Endpoint>.Empty,
                default);

            Assert.That(connection1, Is.EqualTo(connection2));
        }

        [TestCase("icerpc://connectPoolTests.1?transport=coloc", "icerpc://connectPoolTests.2?transport=coloc")]
        [TestCase("icerpc://connectPoolTests:1000?transport=coloc", "icerpc://connectPoolTests:1002?transport=coloc")]
        public async Task ConnectionPool_ConnectionNotReused(
            string endpoint1Str,
            string endpoint2Str,
            bool useTls = false)
        {
            IServiceCollection serviceCollection = new IntegrationTestServiceCollection()
                .AddTransient(typeof(Endpoint), _ => Endpoint.FromString(endpoint1Str));
            if (useTls)
            {
                serviceCollection.UseTls();
            }
            await using ServiceProvider serviceProvider = serviceCollection.BuildServiceProvider();

            await using var server1 = new Server(new ServerOptions
            {
                AuthenticationOptions = serviceProvider.GetService<SslServerAuthenticationOptions>(),
                Endpoint = endpoint1Str,
                MultiplexedServerTransport =
                    serviceProvider.GetRequiredService<IServerTransport<IMultiplexedNetworkConnection>>()
            });
            server1.Listen();

            await using var server2 = new Server(new ServerOptions
            {
                AuthenticationOptions = serviceProvider.GetService<SslServerAuthenticationOptions>(),
                Endpoint = endpoint2Str,
                MultiplexedServerTransport =
                    serviceProvider.GetRequiredService<IServerTransport<IMultiplexedNetworkConnection>>()
            });
            server2.Listen();

            await using var connectionPool = new ConnectionPool(
                new ConnectionOptions
                {
                    AuthenticationOptions = serviceProvider.GetService<SslClientAuthenticationOptions>(),
                    MultiplexedClientTransport =
                        serviceProvider.GetRequiredService<IClientTransport<IMultiplexedNetworkConnection>>()
                });

            Connection connection1 = await connectionPool.GetConnectionAsync(
                server1.Endpoint,
                ImmutableList<Endpoint>.Empty,
                default);

            Connection connection2 = await connectionPool.GetConnectionAsync(
                server2.Endpoint,
                ImmutableList<Endpoint>.Empty,
                default);

            Assert.That(connection1, Is.Not.EqualTo(connection2));
        }
    }
}
