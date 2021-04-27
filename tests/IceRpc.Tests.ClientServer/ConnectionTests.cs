// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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
            await WithServerAsync(async (Server server, IConnectionTestPrx prx) =>
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
            await using var server = new Server
            {
                Communicator = communicator,
                HasColocEndpoint = false,
                Endpoint = GetTestEndpoint(transport: transport, protocol: protocol),
                ConnectionOptions = new()
                {
                    AcceptNonSecure = NonSecure.Always
                }
            };
            server.Listen();

            var prx = IConnectionTestPrx.Parse(
                GetTestProxy("test", transport: transport, protocol: protocol),
                communicator);

            if (transport == "udp")
            {
                prx.NonSecure = NonSecure.Always;
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

            await using var server = new Server
            {
                Communicator = serverCommunicator,
                HasColocEndpoint = false,
                Dispatcher = new ConnectionTest(),
                Endpoint = GetTestEndpoint(protocol: protocol),
                ConnectionOptions = new()
                {
                    IdleTimeout = TimeSpan.FromSeconds(2),
                    KeepAlive = false
                }
            };

            server.Listen();

            var prx = IConnectionTestPrx.Parse(GetTestProxy("/test", protocol: protocol), communicator);
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
            await using var server = new Server
            {
                Communicator = serverCommunicator,
                HasColocEndpoint = false,
                Dispatcher = new ConnectionTest(),
                Endpoint = GetTestEndpoint(protocol: protocol)
            };

            server.Listen();

            await using var clientCommunicator = new Communicator(
                connectionOptions: new()
                {
                    IdleTimeout = TimeSpan.FromSeconds(1),
                    KeepAlive = false
                });

            var prx = IConnectionTestPrx.Parse(GetTestProxy("/test", protocol: protocol), clientCommunicator);
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

            await using var server = new Server
            {
                Communicator = serverCommunicator,
                HasColocEndpoint = false,
                Dispatcher = new ConnectionTest(),
                Endpoint = GetTestEndpoint(protocol: protocol),
                ConnectionOptions = new()
                {
                    IdleTimeout = TimeSpan.FromSeconds(1),
                    KeepAlive = true
                }
            };

            server.Listen();

            var prx = IConnectionTestPrx.Parse(GetTestProxy("/test", protocol: protocol), communicator);
            Connection connection = await prx.GetConnectionAsync();

            var semaphore = new SemaphoreSlim(0);
            connection.PingReceived += (sender, args) => semaphore.Release();
            await semaphore.WaitAsync();
            await semaphore.WaitAsync();
            await semaphore.WaitAsync();
        }

        [TestCase(Protocol.Ice1)]
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
                for (int i = 0; i < 5; ++i)
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
            await using var server = new Server
            {
                Communicator = serverCommunicator,
                HasColocEndpoint = false,
                Dispatcher = new ConnectionTest(),
                Endpoint = GetTestEndpoint()
            };

            server.Listen();

            await using var communicator = new Communicator(
                connectionOptions: new()
                {
                    IdleTimeout = TimeSpan.FromSeconds(idleTimeout),
                    KeepAlive = keepAlive,
                });

            var proxy = IConnectionTestPrx.Parse(GetTestProxy("/test"), communicator);
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
            await using var server = new Server
            {
                Communicator = communicator,
                HasColocEndpoint = false,
                Dispatcher = new ConnectionTest(),
                Endpoint = GetTestEndpoint()
                //TaskScheduler = schedulerPair.ExclusiveScheduler
            };

            server.Listen();
            _ = Task.Factory.StartNew(async () => await semaphore.WaitAsync(),
                                      default,
                                      TaskCreationOptions.None,
                                      schedulerPair.ExclusiveScheduler);
            await Task.Delay(500); // Give time to the previous task to put the server on hold
            var prx = IConnectionTestPrx.Parse(GetTestProxy("/"), communicator);
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
            await using var server = new Server
            {
                Communicator = communicator1,
                HasColocEndpoint = false,
                Dispatcher = new ConnectionTest(),
                Endpoint = GetTestEndpoint(),
                //TaskScheduler = schedulerPair.ExclusiveScheduler
            };
            server.Listen();

            var prx1 = IConnectionTestPrx.Parse(GetTestProxy("/"), communicator1);
            // No close timeout
            var prx2 = IConnectionTestPrx.Parse(GetTestProxy("/"), communicator2);
            Assert.AreEqual(Protocol.Ice2, prx1.Protocol);
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

        [Test]
        public async Task Connection_GracefullyClose()
        {
            await WithServerAsync(async (Server server, IConnectionTestPrx prx) =>
            {
                ProgressCallback cb;
                // Remote case: send multiple opWithPayload, followed by a close and followed by multiple opWithPaylod.
                // The goal is to make sure that none of the opWithPayload fail even if the server closes the
                // connection gracefully in between.
                byte[] seq = new byte[1024 * 10];
                int maxQueue = 2;
                bool done = false;
                while (!done && maxQueue < 50)
                {
                    done = true;
                    await prx.IcePingAsync();
                    var results = new List<Task>();
                    for (int i = 0; i < maxQueue; ++i)
                    {
                        results.Add(prx.OpWithPayloadAsync(seq));
                    }

                    cb = new ProgressCallback();
                    _ = prx.CloseAsync(CloseMode.Gracefully, progress: cb);

                    if (!cb.Sent)
                    {
                        for (int i = 0; i < maxQueue; i++)
                        {
                            cb = new ProgressCallback();
                            results.Add(prx.OpWithPayloadAsync(seq, progress: cb));
                            if (cb.Sent)
                            {
                                done = false;
                                maxQueue *= 2;
                                break;
                            }
                        }
                    }
                    else
                    {
                        maxQueue *= 2;
                        done = false;
                    }

                    await Task.WhenAll(results);
                }

                // Local case: start an operation and then close the connection gracefully on the client side without
                // waiting for the pending invocation to complete. There will be no retry and we expect the invocation
                // to fail with ConnectionClosedException.
                await using var connection = await Connection.CreateAsync(prx.Endpoints[0], prx.Communicator);
                cb = new ProgressCallback();
                IConnectionTestPrx fixedPrx = prx.Clone();
                fixedPrx.Connection = connection;
                fixedPrx.Endpoints = ImmutableList<Endpoint>.Empty;

                Task t = fixedPrx.StartDispatchAsync(progress: cb);
                await cb.Completed.Task; // Ensure the request was sent before closing the connection.
                _ = connection.GoAwayAsync();
                Assert.ThrowsAsync<ConnectionClosedException>(async () => await t);
                await prx.FinishDispatchAsync();

                // Remote case: the server closes the connection gracefully, which means the connection will not
                // be closed until all pending dispatched requests have completed.
                Connection con = await prx.GetConnectionAsync();
                cb = new ProgressCallback();
                var closed = new TaskCompletionSource<object?>();
                con.Closed += (sender, args) => closed.SetResult(null);
                t = prx.SleepAsync(100);
                _ = prx.CloseAsync(CloseMode.Gracefully); // Close is delayed until sleep completes.
                await t;
                await closed.Task;
            });
        }

        [Test]
        public async Task Connection_ForcefullyClose()
        {
            await WithServerAsync(async (Server server, IConnectionTestPrx prx) =>
            {
                // Local case: start an operation and then close the connection forcefully on the client side.
                // There will be no retry and we expect the invocation to fail with ConnectionClosedException.
                await prx.IcePingAsync();
                Connection con = await prx.GetConnectionAsync();
                var cb = new ProgressCallback();
                Task t = prx.StartDispatchAsync(progress: cb);
                await cb.Completed.Task; // Ensure the request was sent before we close the connection.
                _ = con.AbortAsync();

                Assert.ThrowsAsync<ConnectionClosedException>(async () => await t);
                prx.FinishDispatch();

                // Remote case: the server closes the connection forcefully. This causes the request to fail with
                // a ConnectionLostException. Since the close() operation is not idempotent, the client will not
                // retry.
                Assert.ThrowsAsync<ConnectionLostException>(async () => await prx.CloseAsync(CloseMode.Forcefully));
            });
        }

        private async Task WithServerAsync(
            Func<Server, IConnectionTestPrx, Task> closure,
            Protocol protocol = Protocol.Ice2)
        {
            await using var communicator = new Communicator();
            await using var server = new Server
            {
                Communicator = communicator,
                HasColocEndpoint = false,
                Dispatcher = new ConnectionTest(),
                Endpoint = GetTestEndpoint(protocol: protocol)
            };

            server.Listen();
            await closure(server, server.CreateProxy<IConnectionTestPrx>("/test"));
        }

        class ConnectionTest : IAsyncConnectionTest
        {
            private readonly SemaphoreSlim _semaphore = new(0);
            private TaskCompletionSource<object?>? _pending;

            public ValueTask CloseAsync(CloseMode mode, Dispatch dispatch, CancellationToken cancel)
            {
                if (mode == CloseMode.Gracefully)
                {
                    dispatch.Connection.GoAwayAsync(cancel: cancel);
                }
                else
                {
                    dispatch.Connection.AbortAsync();
                }
                return default;
            }

            public async ValueTask EnterAsync(Dispatch _, CancellationToken cancel) =>
                await _semaphore.WaitAsync(cancel);

            public ValueTask FinishDispatchAsync(Dispatch dispatch, CancellationToken cancel)
            {
                if (_pending != null) // Pending might not be set yet if startDispatch is dispatch out-of-order
                {
                    _pending.SetResult(null);
                    _pending = null;
                }
                return default;
            }

            public async ValueTask InitiatePingAsync(Dispatch dispatch, CancellationToken cancel) =>
                await dispatch.Connection.PingAsync(cancel: cancel);

            public ValueTask OpWithPayloadAsync(byte[] seq, Dispatch dispatch, CancellationToken cancel) => default;

            public ValueTask ReleaseAsync(Dispatch dispatch, CancellationToken cancel)
            {
                _semaphore.Release();
                return default;
            }

            public async ValueTask SleepAsync(int ms, Dispatch dispatch, CancellationToken cancel) =>
                await Task.Delay(TimeSpan.FromMilliseconds(ms), cancel);

            public ValueTask StartDispatchAsync(Dispatch dispatch, CancellationToken cancel)
            {
                if (_pending != null)
                {
                    _pending.SetResult(null);
                }
                _pending = new TaskCompletionSource<object?>();
                return new ValueTask(_pending.Task);
            }
        }

        public class ProgressCallback : IProgress<bool>
        {
            public bool Sent
            {
                get
                {
                    lock (_mutex)
                    {
                        return _sent;
                    }
                }
                set
                {
                    lock (_mutex)
                    {
                        _sent = value;
                    }
                }
            }

            public bool SentSynchronously
            {
                get
                {
                    lock (_mutex)
                    {
                        return _sentSynchronously;
                    }
                }
                set
                {
                    lock (_mutex)
                    {
                        Completed.SetResult(null);
                        _sentSynchronously = value;
                    }
                }
            }

            public TaskCompletionSource<object?> Completed { get; } = new();

            private readonly object _mutex = new();
            private bool _sent;
            private bool _sentSynchronously;

            public void Report(bool value)
            {
                SentSynchronously = value;
                Sent = true;
            }
        }
    }
}
