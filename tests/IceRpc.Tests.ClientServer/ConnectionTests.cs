// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.ClientServer
{
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    [Timeout(10000)]
    public class ConnectionTests : ClientServerBaseTest
    {
        [Test]
        public async Task Connection_ClosedEvent()
        {
            await WithServerAsync(async (Server server, IConnectionTestServicePrx prx) =>
            {
                var connection = (await prx.GetConnectionAsync()) as IPConnection;
                Assert.IsNotNull(connection);
                bool called = false;
                connection!.Closed += (sender, args) => called = true;
                await connection.GoAwayAsync();
                Assert.IsTrue(called);
            });
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
                    Endpoints = GetTestEndpoint(transport: transport, protocol: protocol),
                    AcceptNonSecure = NonSecure.Always
                });
            await server.ActivateAsync();

            var prx = IConnectionTestServicePrx.Parse(
                GetTestProxy("test", transport: transport, protocol: protocol),
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
            await using var communicator = new Communicator();
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
                    Endpoints = GetTestEndpoint()
                });

            server.Add("test", new ConnectionTestService());
            await server.ActivateAsync();

            bool closed = false;
            int heartbeat = 0;

            var prx = IConnectionTestServicePrx.Parse(GetTestProxy("test"), communicator);
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
            await using var serverCommunicator = new Communicator();
            await using var server = new Server(
                serverCommunicator,
                new ServerOptions()
                {
                    ColocationScope = ColocationScope.None,
                    Endpoints = GetTestEndpoint()
                });

            server.Add("test", new ConnectionTestService());
            await server.ActivateAsync();

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
            await using var communicator = new Communicator();
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
                    Endpoints = GetTestEndpoint()
                });

            server.Add("test", new ConnectionTestService());
            await server.ActivateAsync();

            bool closed = false;
            int heartbeat = 0;

            var prx = IConnectionTestServicePrx.Parse(GetTestProxy("test"), communicator);
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
            await WithServerAsync(async (server, prx) =>
            {
                var connection = (await prx.GetConnectionAsync()) as IPConnection;
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
                await prx.InitiatePingAsync();
                await prx.InitiatePingAsync();
                await prx.InitiatePingAsync();
                await prx.InitiatePingAsync();
                await prx.InitiatePingAsync();

                lock (mutex)
                {
                    while (called < 5)
                    {
                        Monitor.Wait(mutex);
                    }
                }

                Assert.AreEqual(5, called);
            });
        }

        [TestCase(10, true)]
        [TestCase(50, false)]
        public async Task Connection_SetAcm(int idleTimeout, bool keepAlive)
        {
            await using var serverCommunicator = new Communicator();
            await using var server = new Server(
                serverCommunicator,
                new ServerOptions()
                {
                    ColocationScope = ColocationScope.None,
                    Endpoints = GetTestEndpoint()
                });

            server.Add("test", new ConnectionTestService());
            await server.ActivateAsync();

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

        [Test]
        public async Task Connection_ConnectTimeout()
        {
            var semaphore = new SemaphoreSlim(0);
            var schedulerPair = new ConcurrentExclusiveSchedulerPair(TaskScheduler.Default);
            await using var communicator = new Communicator(
                new Dictionary<string, string>
                {
                    { "Ice.ConnectTimeout", "100ms" }
                });
            await using var server = new Server(communicator,
                                                new()
                                                {
                                                    ColocationScope = ColocationScope.None,
                                                    Endpoints = GetTestEndpoint(),
                                                    TaskScheduler = schedulerPair.ExclusiveScheduler
                                                });
            server.Add("test", new ConnectionTestService());
            await server.ActivateAsync();
            _ = Task.Factory.StartNew(() => semaphore.Wait(),
                                      default,
                                      TaskCreationOptions.None,
                                      schedulerPair.ExclusiveScheduler);
            await Task.Delay(200); // Give time to the previous task to put the server on hold
            var prx = IConnectionTestServicePrx.Parse(GetTestProxy("test"), communicator);
            Assert.ThrowsAsync<ConnectTimeoutException>(async () => await prx.IcePingAsync());
            semaphore.Release();
        }

        [Test]
        public async Task Connection_CloseTimeout()
        {
            var schedulerPair = new ConcurrentExclusiveSchedulerPair(TaskScheduler.Default);
            await using var communicator1 = new Communicator(
                new Dictionary<string, string>
                {
                    { "Ice.CloseTimeout", "100ms" }
                });
            await using var communicator2 = new Communicator(); // No close timeout
            await using var server = new Server(communicator1,
                                                new()
                                                {
                                                    ColocationScope = ColocationScope.None,
                                                    Endpoints = GetTestEndpoint(),
                                                    TaskScheduler = schedulerPair.ExclusiveScheduler
                                                });
            server.Add("test", new ConnectionTestService());
            await server.ActivateAsync();

            var prx1 = IConnectionTestServicePrx.Parse(GetTestProxy("test"), communicator1);
            // No close timeout
            var prx2 = IConnectionTestServicePrx.Parse(GetTestProxy("test"), communicator2);

            Connection connection1 = await prx1.GetConnectionAsync();
            Connection connection2 = await prx2.GetConnectionAsync();

            using var serverSemaphore = new SemaphoreSlim(0);
            _ = Task.Factory.StartNew(() => serverSemaphore.Wait(),
                                      default,
                                      TaskCreationOptions.None,
                                      schedulerPair.ExclusiveScheduler);
            await Task.Delay(200); // Give time to the previous task to put the server on hold

            // Make sure there's no ReadAsync pending
            _ = prx1.IcePingAsync();
            _ = prx2.IcePingAsync();

            using var clientSemaphore = new SemaphoreSlim(0);
            connection1.Closed += (sender, args) => clientSemaphore.Release();
            _ = connection1.GoAwayAsync();
            Assert.IsTrue(clientSemaphore.Wait(1000));
            Assert.AreEqual(0, clientSemaphore.CurrentCount);

            connection2.Closed += (sender, args) => clientSemaphore.Release();
            _ = connection2.GoAwayAsync();
            Assert.IsFalse(clientSemaphore.Wait(1000));

            serverSemaphore.Release();
        }

        private async Task WithServerAsync(Func<Server, IConnectionTestServicePrx, Task> closure)
        {
            await using var communicator = new Communicator();
            await using var server = new Server(
                communicator,
                new ServerOptions()
                {
                    ColocationScope = ColocationScope.None,
                    Endpoints = GetTestEndpoint()
                });
            var prx = server.Add("test", new ConnectionTestService(), IConnectionTestServicePrx.Factory);
            await server.ActivateAsync();
            await closure(server, prx);
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
