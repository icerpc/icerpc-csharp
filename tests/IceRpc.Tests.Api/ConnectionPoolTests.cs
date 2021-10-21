// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using NUnit.Framework;
using System.Collections.Immutable;

namespace IceRpc.Tests.Api
{
    [Timeout(5000)]
    public class ConnectionPoolTests
    {
        [Test]
        public async Task ConnectionPool_Dispatcher()
        {
            await using var server = new Server()
            {
                Endpoint = "ice+coloc://foo"
            };
            server.Listen();

            var dispatcher = new InlineDispatcher((request, cancel) => default);
            await using var connectionPool = new ConnectionPool()
            {
                Dispatcher = dispatcher
            };

            Connection connection = await connectionPool.GetConnectionAsync(
                server.Endpoint,
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
                await using var server = new Server()
                {
                    Endpoint = "ice+coloc://foo"
                };
                server.Listen();

                var connectionPool = new ConnectionPool();
                _ = await connectionPool.GetConnectionAsync(server.Endpoint, ImmutableList<Endpoint>.Empty, default);
                await connectionPool.DisposeAsync();
                await connectionPool.DisposeAsync();
            }
        }

        [Test]
        public async Task ConnectionPool_ConnectionOptions()
        {
            await using var server = new Server()
            {
                Endpoint = "ice+coloc://connectPoolTests"
            };
            server.Listen();

            await using var connectionPool = new ConnectionPool()
            {
                ConnectionOptions = new()
                {
                    IncomingFrameMaxSize = 2048,
                    KeepAlive = true
                }
            };

            Connection connection = await connectionPool.GetConnectionAsync(
                server.Endpoint,
                ImmutableList<Endpoint>.Empty,
                default);

            Assert.That(connection.Options.KeepAlive, Is.True);
            Assert.That(connection.Options.IncomingFrameMaxSize, Is.EqualTo(2048));
        }

        [Test]
        public async Task ConnectionPool_ConnectionReused()
        {
            await using var server = new Server()
            {
                Endpoint = "ice+coloc://connectPoolTests"
            };
            server.Listen();

            await using var connectionPool = new ConnectionPool();

            Connection connection = await connectionPool.GetConnectionAsync(
                server.Endpoint,
                ImmutableList<Endpoint>.Empty,
                default);

            Connection connection1 = await connectionPool.GetConnectionAsync(
                server.Endpoint,
                ImmutableList<Endpoint>.Empty,
                default);

            Connection connection2 = await connectionPool.GetConnectionAsync(
                server.Endpoint,
                ImmutableList<Endpoint>.Empty,
                default);

            Assert.That(connection1, Is.EqualTo(connection2));
        }

        [TestCase("ice+coloc://connectPoolTests.1", "ice+coloc://connectPoolTests.2")]
        [TestCase("ice+coloc://connectPoolTests:1000", "ice+coloc://connectPoolTests:1002")]
        [TestCase("ice+tcp://127.0.0.1:0?tls=true", "ice+tls://127.0.0.1:0?tls=false")]
        [TestCase("ice+tcp://127.0.0.1:0?tls=true", "ice+tls://127.0.0.1:0")]
        public async Task ConnectionPool_ConnectionNotReused(string endpoint1Str, string endpoint2Str)
        {
            Endpoint endpoint1 = endpoint1Str;
            Endpoint endpoint2 = endpoint2Str;

            IServerTransport<IMultiplexedNetworkConnection> serverTransport = Server.DefaultMultiplexedServerTransport;
            IClientTransport<IMultiplexedNetworkConnection> clientTransport =
                Connection.DefaultMultiplexedClientTransport;

            if (endpoint1.Transport == "tcp")
            {
                serverTransport = TestHelper.GetSecureMultiplexedServerTransport();
                clientTransport = TestHelper.GetSecureMultiplexedClientTransport();
            }

            await using var server1 = new Server()
            {
                MultiplexedServerTransport = serverTransport,
                Endpoint = endpoint1
            };
            server1.Listen();

            await using var server2 = new Server()
            {
                MultiplexedServerTransport = serverTransport,
                Endpoint = endpoint2
            };
            server2.Listen();

            await using var connectionPool = new ConnectionPool { MultiplexedClientTransport = clientTransport };

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
