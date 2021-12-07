// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Collections.Immutable;

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
            _serviceProvider = new IntegrationServiceCollection().BuildServiceProvider();
            _clientTransport = _serviceProvider.GetRequiredService<IClientTransport<IMultiplexedNetworkConnection>>();
            _remoteEndpoint = _serviceProvider.GetRequiredService<Server>().Endpoint;
        }

        [OneTimeTearDown]
        public ValueTask DisposeAsync() => _serviceProvider.DisposeAsync();

        [Test]
        public async Task ConnectionPool_Dispatcher()
        {
            var dispatcher = new InlineDispatcher((request, cancel) => default);

            await using var connectionPool = new ConnectionPool()
            {
                Dispatcher = dispatcher,
                MultiplexedClientTransport = _clientTransport
            };

            Connection connection = await connectionPool.GetConnectionAsync(
                _remoteEndpoint,
                ImmutableList<Endpoint>.Empty,
                default);

            Assert.That(connection.Dispatcher, Is.EqualTo(dispatcher));
        }

        [Test]
        public async Task ConnectionPool_Dispose()
        {
            {
                var connectionPool = new ConnectionPool();
                await connectionPool.DisposeAsync();
                await connectionPool.DisposeAsync();
            }

            {
                var connectionPool = new ConnectionPool() { MultiplexedClientTransport = _clientTransport };
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
            await using var connectionPool = new ConnectionPool()
            {
                ConnectionOptions = new()
                {
                    IncomingFrameMaxSize = 2048,
                    KeepAlive = true
                },
                MultiplexedClientTransport = _clientTransport
            };

            Connection connection = await connectionPool.GetConnectionAsync(
                _remoteEndpoint,
                ImmutableList<Endpoint>.Empty,
                default);

            Assert.That(connection.Options.KeepAlive, Is.True);
            Assert.That(connection.Options.IncomingFrameMaxSize, Is.EqualTo(2048));
        }

        [Test]
        public async Task ConnectionPool_ConnectionReused()
        {
            await using var connectionPool = new ConnectionPool() { MultiplexedClientTransport = _clientTransport };

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

        [TestCase("ice+coloc://connectPoolTests.1", "ice+coloc://connectPoolTests.2")]
        [TestCase("ice+coloc://connectPoolTests:1000", "ice+coloc://connectPoolTests:1002")]
        [TestCase("ice+tcp://127.0.0.1:0?tls=true", "ice+tcp://127.0.0.1:0?tls=false")]
        [TestCase("ice+tcp://127.0.0.1:0?tls=true", "ice+tcp://127.0.0.1:0")]
        public async Task ConnectionPool_ConnectionNotReused(string endpoint1Str, string endpoint2Str)
        {
            await using ServiceProvider serviceProvider = new IntegrationServiceCollection()
                .UseTls()
                .AddTransient<Endpoint>(_ => endpoint1Str)
                .BuildServiceProvider();

            await using var server1 = new Server
            {
                Endpoint = endpoint1Str,
                MultiplexedServerTransport =
                    serviceProvider.GetRequiredService<IServerTransport<IMultiplexedNetworkConnection>>()
            };
            server1.Listen();

            await using var server2 = new Server
            {
                Endpoint = endpoint2Str,
                MultiplexedServerTransport =
                    serviceProvider.GetRequiredService<IServerTransport<IMultiplexedNetworkConnection>>()
            };
            server2.Listen();

            await using var connectionPool = new ConnectionPool
            {
                MultiplexedClientTransport =
                    serviceProvider.GetRequiredService<IClientTransport<IMultiplexedNetworkConnection>>()
            };

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
