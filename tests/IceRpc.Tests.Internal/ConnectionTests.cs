// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.Internal
{
    [Parallelizable(ParallelScope.All)]
    [Timeout(30000)]
    public class ConnectionTests
    {
        /// <summary>The connection factory is a small helper to allow creating a client and server connection
        /// directly from the transport API rather than going through the Communicator/Server APIs.</summary>
        private class ConnectionFactory : IAsyncDisposable
        {
            public Connection Client
            {
                get
                {
                    if (_cachedClientConnection == null)
                    {
                        (_cachedServerConnection, _cachedClientConnection) = AcceptAndConnectAsync().Result;
                    }
                    return _cachedClientConnection!;
                }
            }

            public Endpoint Endpoint { get; }

            public ILogger Logger => _server.Logger;

            public Connection Server
            {
                get
                {
                    if (_cachedServerConnection == null)
                    {
                        (_cachedServerConnection, _cachedClientConnection) = AcceptAndConnectAsync().Result;
                    }
                    return _cachedServerConnection!;
                }
            }

            private Connection? _cachedClientConnection;
            private Connection? _cachedServerConnection;
            private readonly Communicator _communicator;
            private readonly Server _server;

            public async Task<(Connection, Connection)> AcceptAndConnectAsync()
            {
                Connection clientConnection;
                Connection serverConnection;

                if (Endpoint.IsDatagram)
                {
                    serverConnection = Endpoint.CreateDatagramServerConnection(_server);
                    clientConnection =
                        await serverConnection.Endpoint.ConnectAsync(
                            options: _communicator.ConnectionOptions,
                            _server.Logger,
                            default);
                    await clientConnection.InitializeAsync(default);
                    await serverConnection.InitializeAsync(default);
                }
                else
                {
                    using IAcceptor acceptor = Endpoint.Acceptor(_server);
                    Task<Connection> serverTask = AcceptAsync(acceptor);
                    Task<Connection> clientTask = ConnectAsync(acceptor.Endpoint);
                    serverConnection = await serverTask;
                    clientConnection = await clientTask;
                }

                return (serverConnection, clientConnection);

                async Task<Connection> AcceptAsync(IAcceptor acceptor)
                {
                    Connection connection = await acceptor.AcceptAsync();
                    await connection.AcceptAsync(null, default);
                    await connection.InitializeAsync(default);
                    return connection;
                }

                async Task<Connection> ConnectAsync(Endpoint endpoint)
                {
                    Connection connection = await endpoint.ConnectAsync(
                        _communicator.ConnectionOptions,
                        _server.Logger,
                        default);
                    await connection.InitializeAsync(default);
                    return connection;
                }
            }

            public async Task<Connection> ConnectAsync()
            {
                Connection connection = await Endpoint.ConnectAsync(
                    options: _communicator.ConnectionOptions,
                    _server.Logger,
                    default);
                await connection.InitializeAsync(default);
                return connection;
            }

            // TODO: add Connection.CreateProxy?
            public IServicePrx CreateProxy(Connection connection) =>
                IServicePrx.Factory.Create(
                    "/foo",
                    Endpoint.Protocol,
                    Endpoint.Protocol.GetEncoding(),
                    endpoint: null,
                    altEndpoints: ImmutableList<Endpoint>.Empty,
                    connection,
                    new ProxyOptions { Communicator = _communicator });

            public async ValueTask DisposeAsync()
            {
                if (_cachedClientConnection != null)
                {
                    await _cachedClientConnection.DisposeAsync();
                    await _cachedServerConnection!.DisposeAsync();
                }
                await _server.DisposeAsync();
                await _communicator.DisposeAsync();
            }

            public ConnectionFactory(
                string transport = "coloc",
                Protocol protocol = Protocol.Ice2,
                OutgoingConnectionOptions? clientConnectionOptions = null,
                IncomingConnectionOptions? serverConnectionOptions = null,
                IDispatcher? dispatcher = null)
            {
                using ILoggerFactory loggerFactory = LoggerFactory.Create(
                    builder =>
                    {
                        // builder.AddSimpleConsole(configure =>
                        // {
                        //      configure.IncludeScopes = true;
                        //      configure.TimestampFormat = "[HH:mm:ss:fff] ";
                        //      configure.ColorBehavior = LoggerColorBehavior.Disabled;
                        // });
                        // builder.SetMinimumLevel(LogLevel.Debug);
                    });

                _communicator = new Communicator(
                    new Dictionary<string, string>() { { "Ice.InvocationMaxAttempts", "1" } },
                    loggerFactory: loggerFactory,
                    connectionOptions: clientConnectionOptions);

                _server = new Server
                {
                    Communicator = _communicator,
                    ConnectionOptions = serverConnectionOptions ?? new(),
                    Dispatcher = dispatcher,
                    LoggerFactory = loggerFactory,
                };
                _ = _server.Logger;

                if (transport == "coloc")
                {
                    Endpoint = new ColocEndpoint(Guid.NewGuid().ToString(), 4062, protocol);
                }
                else if (transport == "udp" || protocol == Protocol.Ice1)
                {
                    Endpoint = Endpoint.Parse($"{transport} -h 127.0.0.1");
                }
                else
                {
                    Endpoint = Endpoint.Parse($"ice+{transport}://127.0.0.1:0?tls=false");
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task Connection_AbortAsync(bool closeClientSide)
        {
            using var semaphore = new SemaphoreSlim(0);
            await using var factory = new ConnectionFactory(
                "tcp",
                dispatcher: new InlineDispatcher(async (request, cancel) =>
                {
                    await semaphore.WaitAsync(cancel);
                    return OutgoingResponse.WithVoidReturnValue(new Dispatch(request));
                }));

            // Perform an invocation
            IServicePrx proxy = factory.CreateProxy(factory.Client);
            Task pingTask = proxy.IcePingAsync();

            if (closeClientSide)
            {
                await factory.Client.AbortAsync();
                Assert.ThrowsAsync<ConnectionClosedException>(async () => await pingTask);
            }
            else
            {
                await factory.Server.AbortAsync();
                Assert.ThrowsAsync<ConnectionLostException>(async () => await pingTask);
            }
            semaphore.Release();
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task Connection_ClosedEventAsync(bool closeClientSide)
        {
            await using var factory = new ConnectionFactory();

            using var semaphore = new SemaphoreSlim(0);
            factory.Client.Closed += (sender, args) => semaphore.Release();
            factory.Server.Closed += (sender, args) => semaphore.Release();

            await (closeClientSide ? factory.Client : factory.Server).GoAwayAsync();

            await semaphore.WaitAsync();
            await semaphore.WaitAsync();
        }

        [TestCase(Protocol.Ice1, false)]
        [TestCase(Protocol.Ice1, true)]
        [TestCase(Protocol.Ice2, false)]
        [TestCase(Protocol.Ice2, true)]
        public async Task Connection_CloseOnIdleAsync(Protocol protocol, bool idleOnClient)
        {
            await using var factory = new ConnectionFactory(
                "tcp",
                protocol,
                clientConnectionOptions: new()
                {
                    IdleTimeout = idleOnClient ? TimeSpan.FromMilliseconds(500) : TimeSpan.FromHours(1)
                },
                serverConnectionOptions: new()
                {
                    IdleTimeout = idleOnClient ? TimeSpan.FromHours(1) : TimeSpan.FromMilliseconds(500)
                });

            var semaphore = new SemaphoreSlim(0);
            factory.Client.Closed += (sender, args) => semaphore.Release();
            factory.Server.Closed += (sender, args) => semaphore.Release();
            await semaphore.WaitAsync();
            await semaphore.WaitAsync();
        }

        [TestCase(Protocol.Ice1)]
        [TestCase(Protocol.Ice2)]
        public async Task Connection_ConnectTimeoutAsync(Protocol protocol)
        {
            await using var factory = new ConnectionFactory("tcp", protocol: protocol);

            using IAcceptor acceptor = factory.Endpoint.Acceptor(new Server()
            {
                ConnectionOptions = new()
                {
                    TransportOptions = new TcpOptions()
                    {
                        ListenerBackLog = 1
                    }
                }
            });

            // TODO: add test once it's possible to create a connection directly. Right now, the connect timeout
            // is handled by the outgoing connection factory.
        }

        [TestCase("tcp")]
        [TestCase("ws")]
        [TestCase("udp")]
        public async Task Connection_InformationAsync(string transport)
        {
            await using var factory = new ConnectionFactory(transport);

            Assert.That(factory.Client, Is.AssignableTo<IPConnection>());
            Assert.That(factory.Server, Is.AssignableTo<IPConnection>());

            var connection = (IPConnection)factory.Client;
            var serverConnection = (IPConnection)factory.Server;

            Assert.That(connection.RemoteEndpoint, Is.Not.Null);
            Assert.That(connection.LocalEndpoint, Is.Not.Null);

            Assert.AreEqual("127.0.0.1", connection!.Endpoint.Host);
            Assert.That(connection.Endpoint.Port, Is.GreaterThan(0));
            Assert.AreEqual(null, connection.Endpoint["compress"]);
            Assert.That(connection.IsIncoming, Is.False);
            Assert.That(serverConnection.IsIncoming, Is.True);

            Assert.AreEqual(null, connection.Server);
            Assert.AreEqual(connection.Endpoint.Port, connection.RemoteEndpoint!.Port);
            Assert.That(connection.LocalEndpoint!.Port, Is.GreaterThan(0));

            Assert.AreEqual("127.0.0.1", connection.LocalEndpoint!.Address.ToString());
            Assert.AreEqual("127.0.0.1", connection.RemoteEndpoint!.Address.ToString());

            if (transport == "ws")
            {
                Assert.That(connection, Is.AssignableTo<WSConnection>());
                var wsConnection = (WSConnection)connection;

                Assert.AreEqual("websocket", wsConnection.Headers["Upgrade"]);
                Assert.AreEqual("Upgrade", wsConnection.Headers["Connection"]);
                Assert.AreEqual("ice.zeroc.com", wsConnection.Headers["Sec-WebSocket-Protocol"]);
                Assert.That(wsConnection.Headers["Sec-WebSocket-Accept"], Is.Not.Null);
            }
        }

        [TestCase(Protocol.Ice1)]
        [TestCase(Protocol.Ice2)]
        public async Task Connection_IdleTimeoutAsync(Protocol protocol)
        {
            await using var factory = new ConnectionFactory(
                "tcp",
                protocol: protocol,
                clientConnectionOptions: new()
                {
                    IdleTimeout = TimeSpan.FromSeconds(2)
                },
                serverConnectionOptions: new()
                {
                    IdleTimeout = TimeSpan.FromSeconds(3)
                });

            Assert.That(factory.Client.IdleTimeout, Is.EqualTo(TimeSpan.FromSeconds(2)));

            if (protocol == Protocol.Ice1)
            {
                Assert.That(factory.Server.IdleTimeout, Is.EqualTo(TimeSpan.FromSeconds(3)));

                factory.Client.IdleTimeout = TimeSpan.FromSeconds(5);
                Assert.That(factory.Client.IdleTimeout, Is.EqualTo(TimeSpan.FromSeconds(5)));
            }
            else
            {
                Assert.That(factory.Server.IdleTimeout, Is.EqualTo(TimeSpan.FromSeconds(2)));

                // With Ice2 the idle timeout is negotiated on initialization and it can't be updated
                Assert.Throws<NotSupportedException>(() => factory.Client.IdleTimeout = TimeSpan.FromSeconds(5));
            }
        }

        [Test]
        public async Task Connection_IdleTimeout_InvalidAsync()
        {
            await using var factory = new ConnectionFactory();
            Assert.Throws<ArgumentException>(() => factory.Client.IdleTimeout = TimeSpan.Zero);
            Assert.Throws<ArgumentException>(() => factory.Server.IdleTimeout = TimeSpan.Zero);
        }

        [TestCase(Protocol.Ice1)]
        [TestCase(Protocol.Ice2)]
        public async Task Connection_KeepAliveAsync(Protocol protocol)
        {
            await using var factory = new ConnectionFactory(
                protocol: protocol,
                clientConnectionOptions: new()
                {
                    KeepAlive = true
                },
                serverConnectionOptions: new()
                {
                    KeepAlive = true
                });
            Assert.That(factory.Client.KeepAlive, Is.True);
            Assert.That(factory.Server.KeepAlive, Is.True);
        }

        [TestCase(Protocol.Ice1, false)]
        [TestCase(Protocol.Ice1, true)]
        [TestCase(Protocol.Ice2, false)]
        [TestCase(Protocol.Ice2, true)]
        public async Task Connection_KeepAliveOnIdleAsync(Protocol protocol, bool heartbeatOnClient)
        {
            await using var factory = new ConnectionFactory(
                "tcp",
                protocol,
                clientConnectionOptions: new()
                {
                    IdleTimeout = TimeSpan.FromSeconds(1),
                    KeepAlive = heartbeatOnClient
                },
                serverConnectionOptions: new()
                {
                    IdleTimeout = TimeSpan.FromSeconds(1),
                    KeepAlive = !heartbeatOnClient
                });

            var semaphore = new SemaphoreSlim(0);
            if (heartbeatOnClient)
            {
                factory.Client.PingReceived += (sender, args) => Assert.Fail();
                factory.Server.PingReceived += (sender, args) => semaphore.Release();
            }
            else
            {
                factory.Client.PingReceived += (sender, args) => semaphore.Release();
                factory.Server.PingReceived += (sender, args) => Assert.Fail();
            }

            await semaphore.WaitAsync();
            await semaphore.WaitAsync();
        }

        [TestCase(Protocol.Ice1)]
        [TestCase(Protocol.Ice2)]
        public async Task Connection_KeepAliveOnInvocationAsync(Protocol protocol)
        {
            using var dispatchSemaphore = new SemaphoreSlim(0);
            await using var factory = new ConnectionFactory(
                "tcp",
                protocol,
                serverConnectionOptions: new() { IdleTimeout = TimeSpan.FromMilliseconds(100) },
                dispatcher: new InlineDispatcher(async (request, cancel) =>
                {
                    await dispatchSemaphore.WaitAsync(cancel);
                    return OutgoingResponse.WithVoidReturnValue(new Dispatch(request));
                }));

            // Perform an invocation
            IServicePrx proxy = factory.CreateProxy(factory.Client);
            Task pingTask = proxy.IcePingAsync();

            // Make sure we receive few pings while the invocation is pending.
            var semaphore = new SemaphoreSlim(0);
            factory.Client.PingReceived += (sender, args) => semaphore.Release();
            await semaphore.WaitAsync();
            await semaphore.WaitAsync();
            await semaphore.WaitAsync();

            dispatchSemaphore.Release();

            await pingTask;
        }

        [TestCase(false, Protocol.Ice1)]
        [TestCase(true, Protocol.Ice1)]
        [TestCase(false, Protocol.Ice2)]
        [TestCase(true, Protocol.Ice2)]
        public async Task Connection_GoAwayAsync(bool closeClientSide, Protocol protocol)
        {
            using var waitForDispatchSemaphore = new SemaphoreSlim(0);
            using var dispatchSemaphore = new SemaphoreSlim(0);
            await using var factory = new ConnectionFactory(
                "tcp",
                protocol,
                dispatcher: new InlineDispatcher(async (request, cancel) =>
                {
                    waitForDispatchSemaphore.Release();
                    await dispatchSemaphore.WaitAsync(cancel);
                    return OutgoingResponse.WithVoidReturnValue(new Dispatch(request));
                }));

            // Perform an invocation
            IServicePrx proxy = factory.CreateProxy(factory.Client);
            Task pingTask = proxy.IcePingAsync();
            await waitForDispatchSemaphore.WaitAsync();

            if (closeClientSide)
            {
                Task goAwayTask = factory.Client.GoAwayAsync("client message");

                // GoAway waits for the server-side connection closure, which can't occur until all the dispatch
                // complete on the connection. We release the dispatch here to ensure GoAway completes.
                // TODO: with Ice2, calling GoAwayAsync on the client side should cancel the dispatch since the
                // connection doesn't wait for the response?
                Assert.That(dispatchSemaphore.Release(), Is.EqualTo(0));

                await goAwayTask;

                // Next invocation on the connection should throw the ConnectionClosedException
                ConnectionClosedException? ex =
                    Assert.ThrowsAsync<ConnectionClosedException>(async () => await pingTask);
                Assert.That(ex, Is.Not.Null);
                Assert.That(ex!.Message, Is.EqualTo("client message"));
                Assert.That(ex.IsClosedByPeer, Is.False);
                Assert.That(ex.RetryPolicy, Is.EqualTo(RetryPolicy.AfterDelay(TimeSpan.Zero)));
            }
            else
            {
                // GoAway waits for the client-side connection closure, which can't occur until all the invocations
                // complete on the connection. We release the dispatch here and ensure GoAway completes.
                Task goAwayTask = factory.Server.GoAwayAsync("server message");
                Assert.That(dispatchSemaphore.Release(), Is.EqualTo(0));
                await goAwayTask;

                // Ensure the invocation is successful
                Assert.DoesNotThrowAsync(async () => await pingTask);

                // Next invocation on the connection should throw the ConnectionClosedException
                ConnectionClosedException? ex =
                    Assert.ThrowsAsync<ConnectionClosedException>(async () => await proxy.IcePingAsync());
                Assert.That(ex, Is.Not.Null);

                // TODO: after connetion refactoring, should a non-resumable connection remember if
                // it was closed by the peer and the closure reason? The server message isn't very
                // useful right now except for tracing.
                Assert.That(ex!.Message, Is.EqualTo("cannot access closed connection"));
                Assert.That(ex.IsClosedByPeer, Is.False);
                Assert.That(ex.RetryPolicy, Is.EqualTo(RetryPolicy.AfterDelay(TimeSpan.Zero)));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task Connection_GoAway_TimeoutAsync(bool closeClientSide)
        {
            using var semaphore = new SemaphoreSlim(0);
            using var waitForDispatchSemaphore = new SemaphoreSlim(0);
            await using var factory = new ConnectionFactory(
                "tcp",
                clientConnectionOptions: new()
                {
                    CloseTimeout = closeClientSide ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(60)
                },
                serverConnectionOptions: new()
                {
                    CloseTimeout = closeClientSide ? TimeSpan.FromSeconds(60) : TimeSpan.FromSeconds(1)
                },
                dispatcher: new InlineDispatcher(async (request, cancel) =>
                {
                    waitForDispatchSemaphore.Release();
                    await semaphore.WaitAsync(cancel);
                    return OutgoingResponse.WithVoidReturnValue(new Dispatch(request));
                }));

            // Perform an invocation
            IServicePrx proxy = factory.CreateProxy(factory.Client);
            Task pingTask = proxy.IcePingAsync();
            await waitForDispatchSemaphore.WaitAsync();

            if (closeClientSide)
            {
                // GoAway should trigger the abort of the connection after the close timeout
                await factory.Client.GoAwayAsync();

                // The client side aborts the invocation as soon as the client-side connection is shutdown.
                Assert.ThrowsAsync<ConnectionClosedException>(async () => await pingTask);
            }
            else
            {
                // GoAway should trigger the abort of the connection after the close timeout
                await factory.Server.GoAwayAsync();

                // The server forcefully close the connection after the close timeout.
                Assert.ThrowsAsync<ConnectionLostException>(async () => await pingTask);
            }
            semaphore.Release();
        }
    }
}
