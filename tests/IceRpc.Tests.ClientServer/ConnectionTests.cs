// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ZeroC.Ice;

namespace IceRpc.Tests.ClientServer
{
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    [Parallelizable(scope: ParallelScope.All)]
    public class ConnectionTests : ClientServerBaseTest
    {
        private IConnectionTestServicePrx Prx { get; }

        public ConnectionTests() =>
            Prx = Server.Add("test", new ConnectionTestService(), IConnectionTestServicePrx.Factory);

        [Test]
        public async Task Connection_ClosedEvent()
        {
            var connection = (await Prx.GetConnectionAsync()) as IPConnection;
            Assert.IsNotNull(connection);
            bool called = false;
            connection!.Closed += (sender, args) =>
            {
                called = true;
            };
            await connection.GoAwayAsync();
            Assert.IsTrue(called);
        }

        [TestCase("tcp", Protocol.Ice1)]
        [TestCase("ws", Protocol.Ice1)]
        [TestCase("udp", Protocol.Ice1)]
        public async Task Connection_Information(string transport, Protocol protocol)
        {
            await using var communicator = new Communicator();
            await using var server = new Server(
                communicator,
                new ServerOptions()
                {
                    ColocationScope = ColocationScope.None,
                    Endpoints = GetTestEndpoint(port: 1, transport: transport, protocol: protocol),
                    AcceptNonSecure = NonSecure.Always
                });
            await server.ActivateAsync();

            var prx = IConnectionTestServicePrx.Parse(
                GetTestProxy("test", port: 1, transport: transport, protocol: protocol),
                communicator);

            if (transport == "udp")
            {
                prx = prx.Clone(preferNonSecure: NonSecure.Always, oneway: true);
            }


            var connection = (await prx.GetConnectionAsync()) as IPConnection;
            Assert.NotNull(connection);
            Assert.NotNull(connection!.RemoteEndpoint);
            Assert.NotNull(connection!.LocalEndpoint);

            Assert.AreEqual("127.0.0.1", connection!.Endpoint.Host);
            Assert.IsTrue(connection.Endpoint.Port > 0);
            Assert.AreEqual(null, connection.Endpoint["compress"]);
            Assert.IsFalse(connection.IsIncoming);

            Assert.AreEqual(null, connection.Server);
            Assert.AreEqual(connection.Endpoint.Port, connection.RemoteEndpoint!.Port);
            Assert.IsTrue(connection.LocalEndpoint!.Port > 0);

            Assert.AreEqual("127.0.0.1", connection.LocalEndpoint!.Address.ToString());
            Assert.AreEqual("127.0.0.1", connection.RemoteEndpoint!.Address.ToString());


            if (transport == "ws")
            {
                WSConnection wsConnection = (WSConnection)connection;
                Assert.IsNotNull(wsConnection);
                Assert.AreEqual("websocket", wsConnection.Headers["Upgrade"]);
                Assert.AreEqual("Upgrade", wsConnection.Headers["Connection"]);
                Assert.AreEqual("ice.zeroc.com", wsConnection.Headers["Sec-WebSocket-Protocol"]);
                Assert.IsNotNull(wsConnection.Headers["Sec-WebSocket-Accept"]);
            }
        }

        [Test]
        public async Task Connection_InvocationHeartbeat()
        {
            await using var serverCommunicator = new Communicator(
                new Dictionary<string, string>()
                {
                    { "Ice.IdleTimeout", "2s" },
                    { "Ice.KeepAlive", "0" }
                });

            await using var server = new Server(
                serverCommunicator,
                new ServerOptions()
                {
                    ColocationScope = ColocationScope.None,
                    Endpoints = GetTestEndpoint(port: 1)
                });

            server.Add("test", new ConnectionTestService());
            await server.ActivateAsync();

            bool closed = false;
            int heartbeat = 0;

            var prx = IConnectionTestServicePrx.Parse(GetTestProxy("test", port: 1), Communicator);
            Connection connection = await prx.GetConnectionAsync();

            connection.Closed += (sender, args) => closed = true;

            connection.PingReceived += (sender, args) => ++heartbeat;

            await prx.SleepAsync(4);

            Assert.IsFalse(closed);
            Assert.IsTrue(heartbeat >= 2);
        }

        [Test]
        public async Task Connection_CloseOnIdle()
        {
            await using var clientCommunicator = new Communicator(
                new Dictionary<string, string>()
                {
                    { "Ice.IdleTimeout", "1s" },
                    { "Ice.KeepAlive", "0" }
                });

            var prx = IConnectionTestServicePrx.Parse(GetTestProxy("test"), clientCommunicator);
            Connection connection = await prx.GetConnectionAsync();

            bool closed = false;
            var mutex = new object();
            connection.Closed += (sender, args) =>
            {
                lock (mutex)
                {
                    closed = true;
                    Monitor.PulseAll(mutex);
                }
            };

            lock (mutex)
            {
                if (!closed)
                {
                    Monitor.Wait(mutex);
                }
            }
            Assert.IsTrue(closed);
        }

        [Test]
        public async Task Connection_HeartbeatOnIdle()
        {
            await using var serverCommunicator = new Communicator(
                new Dictionary<string, string>()
                {
                    { "Ice.IdleTimeout", "1s" },
                    { "Ice.KeepAlive", "1" }
                });

            await using var server = new Server(
                serverCommunicator,
                new ServerOptions()
                {
                    ColocationScope = ColocationScope.None,
                    Endpoints = GetTestEndpoint(port: 1)
                });

            server.Add("test", new ConnectionTestService());
            await server.ActivateAsync();

            bool closed = false;
            int heartbeat = 0;

            var prx = IConnectionTestServicePrx.Parse(GetTestProxy("test", port: 1), Communicator);
            Connection connection = await prx.GetConnectionAsync();

            connection.Closed += (sender, args) => closed = true;

            connection.PingReceived += (sender, args) => ++heartbeat;

            await Task.Delay(TimeSpan.FromSeconds(3));

            Assert.IsFalse(closed);
            Assert.IsTrue(heartbeat >= 3);
        }

        [Test]
        public async Task Connection_HeartbeatManual()
        {
            var connection = (await Prx.GetConnectionAsync()) as IPConnection;
            Assert.IsNotNull(connection);
            object mutex = new object();
            int called = 0;
            connection!.PingReceived += (sender, args) =>
            {
                lock (mutex)
                {
                    called++;
                    Monitor.PulseAll(mutex);
                }
            };
            await Prx.InitiatePingAsync();
            await Prx.InitiatePingAsync();
            await Prx.InitiatePingAsync();
            await Prx.InitiatePingAsync();
            await Prx.InitiatePingAsync();

            lock (mutex)
            {
                while (called < 5)
                {
                    Monitor.Wait(mutex);
                }
            }

            Assert.AreEqual(5, called);
        }

        [TestCase(10, true)]
        [TestCase(50, false)]
        public async Task Connection_SetAcm(int idleTimeout, bool keepAlive)
        {
            await using var communicator = new Communicator(
                new Dictionary<string, string>()
                {
                    { "Ice.IdleTimeout", $"{idleTimeout}s" },
                    { "Ice.KeepAlive", $"{keepAlive}" }
                });

            var proxy = IConnectionTestServicePrx.Parse(GetTestProxy("test"), communicator);
            Connection connection = await proxy.GetConnectionAsync();
            Assert.AreEqual(TimeSpan.FromSeconds(idleTimeout), connection.IdleTimeout);
            Assert.AreEqual(keepAlive, connection.KeepAlive);

            connection.KeepAlive = !keepAlive;
            Assert.AreEqual(!keepAlive, connection.KeepAlive);
        }

        class ConnectionTestService : IAsyncConnectionTestService
        {
            public async ValueTask InitiatePingAsync(Current current, CancellationToken cancel) =>
                await current.Connection.PingAsync(cancel: cancel);

            public async ValueTask SleepAsync(int seconds, Current current, CancellationToken cancel) =>
                await Task.Delay(TimeSpan.FromSeconds(seconds), cancel);
        }
    }
}
