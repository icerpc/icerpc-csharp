// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.ClientServer
{
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    [Parallelizable(ParallelScope.All)]
    [Timeout(30000)]
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
                    Endpoint = GetTestEndpoint(transport: transport, protocol: protocol),
                    ConnectionOptions = new()
                    {
                        AcceptNonSecure = NonSecure.Always
                    }
                });
            server.Activate();

            var prx = IConnectionTestServicePrx.Parse(
                GetTestProxy("test", transport: transport, protocol: protocol),
                communicator);

            if (transport == "udp")
            {
                prx = prx.Clone(nonSecure: NonSecure.Always, oneway: true);
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

        [TestCase(Protocol.Ice1)]
        [TestCase(Protocol.Ice2)]
        public async Task Connection_InvocationHeartbeat(Protocol protocol)
        {
            await using var communicator = new Communicator();
            await using var serverCommunicator = new Communicator();

            await using var server = new Server(
                serverCommunicator,
                new ServerOptions()
                {
                    ColocationScope = ColocationScope.None,
                    Endpoint = GetTestEndpoint(protocol: protocol),
                    ConnectionOptions = new()
                    {
                        IdleTimeout = TimeSpan.FromSeconds(2),
                        KeepAlive = false
                    }
                });

            server.Add("test", new ConnectionTestService());
            server.Activate();

            var prx = IConnectionTestServicePrx.Parse(GetTestProxy("test", protocol: protocol), communicator);
            Connection connection = await prx.GetConnectionAsync();

            var semaphore = new SemaphoreSlim(0);
            connection.PingReceived += (sender, args) => semaphore.Release();

            Task task = prx.EnterAsync();

            await semaphore.WaitAsync();
            await semaphore.WaitAsync();
            await semaphore.WaitAsync();

            await prx.ReleaseAsync();
            await task;
        }

        [TestCase(Protocol.Ice1)]
        [TestCase(Protocol.Ice2)]
        public async Task Connection_CloseOnIdle(Protocol protocol)
        {
            await using var serverCommunicator = new Communicator();
            await using var server = new Server(
                serverCommunicator,
                new ServerOptions()
                {
                    ColocationScope = ColocationScope.None,
                    Endpoint = GetTestEndpoint(protocol: protocol)
                });

            server.Add("test", new ConnectionTestService());
            server.Activate();

            await using var clientCommunicator = new Communicator(
                connectionOptions: new()
                {
                    IdleTimeout = TimeSpan.FromSeconds(1),
                    KeepAlive = false
                });

            var prx = IConnectionTestServicePrx.Parse(GetTestProxy("test", protocol: protocol), clientCommunicator);
            Connection connection = await prx.GetConnectionAsync();

            var semaphore = new SemaphoreSlim(0);
            connection.Closed += (sender, args) => semaphore.Release();
            await semaphore.WaitAsync();
        }

        [TestCase(Protocol.Ice1)]
        [TestCase(Protocol.Ice2)]
        public async Task Connection_HeartbeatOnIdle(Protocol protocol)
        {
            await using var communicator = new Communicator();
            await using var serverCommunicator = new Communicator();

            await using var server = new Server(
                serverCommunicator,
                new ServerOptions()
                {
                    ColocationScope = ColocationScope.None,
                    Endpoint = GetTestEndpoint(protocol: protocol),
                    ConnectionOptions = new()
                    {
                        IdleTimeout = TimeSpan.FromSeconds(1),
                        KeepAlive = true
                    }
                });

            server.Add("test", new ConnectionTestService());
            server.Activate();

            var prx = IConnectionTestServicePrx.Parse(GetTestProxy("test", protocol: protocol), communicator);
            Connection connection = await prx.GetConnectionAsync();

            var semaphore = new SemaphoreSlim(0);
            connection.PingReceived += (sender, args) => semaphore.Release();
            await semaphore.WaitAsync();
            await semaphore.WaitAsync();
            await semaphore.WaitAsync();
        }

        [TestCase(Protocol.Ice1)]
        [TestCase(Protocol.Ice2)]
        public async Task Connection_HeartbeatManual(Protocol protocol)
        {
            await WithServerAsync(async (server, prx) =>
            {
                var connection = (await prx.GetConnectionAsync()) as IPConnection;
                Assert.IsNotNull(connection);
                var semaphore = new SemaphoreSlim(0);
                connection!.PingReceived += (sender, args) => semaphore.Release();
                await prx.InitiatePingAsync();
                await prx.InitiatePingAsync();
                await prx.InitiatePingAsync();
                await prx.InitiatePingAsync();
                await prx.InitiatePingAsync();
                for(int i = 0; i < 5; ++i)
                {
                    await semaphore.WaitAsync();
                }
            }, protocol);
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
                    Endpoint = GetTestEndpoint()
                });

            server.Add("test", new ConnectionTestService());
            server.Activate();

            await using var communicator = new Communicator(
                connectionOptions: new()
                {
                    IdleTimeout = TimeSpan.FromSeconds(idleTimeout),
                    KeepAlive = keepAlive,
                });

            var proxy = IConnectionTestServicePrx.Parse(GetTestProxy("test"), communicator);
            Connection connection = await proxy.GetConnectionAsync();
            Assert.AreEqual(TimeSpan.FromSeconds(idleTimeout), connection.IdleTimeout);
            Assert.AreEqual(keepAlive, connection.KeepAlive);

            connection.KeepAlive = !keepAlive;
            Assert.AreEqual(!keepAlive, connection.KeepAlive);
        }

        // TODO: This test can't work because the server hold doesn't work
        // [Test]
        public async Task Connection_ConnectTimeout()
        {
            var semaphore = new SemaphoreSlim(0);
            var schedulerPair = new ConcurrentExclusiveSchedulerPair(TaskScheduler.Default);
            await using var communicator = new Communicator(
                connectionOptions: new()
                {
                    ConnectTimeout = TimeSpan.FromMilliseconds(100)
                });
            await using var server = new Server(communicator,
                                                new()
                                                {
                                                    ColocationScope = ColocationScope.None,
                                                    Endpoint = GetTestEndpoint(),
                                                    TaskScheduler = schedulerPair.ExclusiveScheduler
                                                });
            server.Add("test", new ConnectionTestService());
            server.Activate();
            _ = Task.Factory.StartNew(async () => await semaphore.WaitAsync(),
                                      default,
                                      TaskCreationOptions.None,
                                      schedulerPair.ExclusiveScheduler);
            await Task.Delay(500); // Give time to the previous task to put the server on hold
            var prx = IConnectionTestServicePrx.Parse(GetTestProxy("test"), communicator);
            Assert.ThrowsAsync<ConnectTimeoutException>(async () => await prx.IcePingAsync());
            semaphore.Release();
        }

        // TODO: This test can't work because the server hold doesn't work
        // [Test]
        public async Task Connection_CloseTimeout()
        {
            var schedulerPair = new ConcurrentExclusiveSchedulerPair(TaskScheduler.Default);
            await using var communicator1 = new Communicator(
                connectionOptions: new()
                {
                    CloseTimeout = TimeSpan.FromMilliseconds(100)
                });
            await using var communicator2 = new Communicator(
                connectionOptions: new()
                {
                    CloseTimeout = TimeSpan.FromSeconds(60)
                });
            await using var server = new Server(communicator1,
                                                new()
                                                {
                                                    ColocationScope = ColocationScope.None,
                                                    Endpoint = GetTestEndpoint(),
                                                    TaskScheduler = schedulerPair.ExclusiveScheduler
                                                });
            server.Add("test", new ConnectionTestService());
            server.Activate();

            var prx1 = IConnectionTestServicePrx.Parse(GetTestProxy("test"), communicator1);
            // No close timeout
            var prx2 = IConnectionTestServicePrx.Parse(GetTestProxy("test"), communicator2);
            Assert.AreEqual(prx1.Protocol, Protocol.Ice2);
            Connection connection1 = await prx1.GetConnectionAsync();
            Connection connection2 = await prx2.GetConnectionAsync();

            using var serverSemaphore = new SemaphoreSlim(0);
            _ = Task.Factory.StartNew(async () => await serverSemaphore.WaitAsync(),
                                      default,
                                      TaskCreationOptions.None,
                                      schedulerPair.ExclusiveScheduler);
            await Task.Delay(200); // Give time to the previous task to put the server on hold

            // Make sure there's no ReadAsync pending
            _ = prx1.IcePingAsync();
            _ = prx2.IcePingAsync();

            using var clientSemaphore = new SemaphoreSlim(0, 1);
            connection1.Closed += (sender, args) => clientSemaphore.Release();
            _ = connection1.GoAwayAsync();
            await clientSemaphore.WaitAsync();

            connection2.Closed += (sender, args) => clientSemaphore.Release();
            _ = connection2.GoAwayAsync();
            Assert.IsFalse(await clientSemaphore.WaitAsync(1000));

            serverSemaphore.Release();
        }

        private async Task WithServerAsync(
            Func<Server, IConnectionTestServicePrx, Task> closure,
            Protocol protocol = Protocol.Ice2)
        {
            await using var communicator = new Communicator();
            await using var server = new Server(
                communicator,
                new ServerOptions()
                {
                    ColocationScope = ColocationScope.None,
                    Endpoint = GetTestEndpoint(protocol: protocol)
                });
            var prx = server.Add("test", new ConnectionTestService(), IConnectionTestServicePrx.Factory);
            server.Activate();
            await closure(server, prx);
        }

        class ConnectionTestService : IAsyncConnectionTestService
        {
            private readonly SemaphoreSlim _semaphore = new(0);

            public async ValueTask InitiatePingAsync(Current current, CancellationToken cancel) =>
                await current.Connection.PingAsync(cancel: cancel);

            public async ValueTask EnterAsync(Current _, CancellationToken cancel) =>
                await _semaphore.WaitAsync(cancel);

            public ValueTask ReleaseAsync(Current current, CancellationToken cancel)
            {
                _semaphore.Release();
                return default;
            }
        }
    }
}
