// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Transports;
using NUnit.Framework;
using System;
using System.Collections.Immutable;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
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
            public Connection ClientConnection
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

            public ClientConnectionOptions ClientConnectionOptions { get; }

            public Endpoint Endpoint { get; }

            public Connection ServerConnection
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

            public IServicePrx ServicePrx
            {
                get
                {
                    var prx = IceRpc.ServicePrx.FromConnection(ClientConnection);
                    var pipeline = new Pipeline();
                    pipeline.UseLogger(LogAttributeLoggerFactory.Instance);
                    prx.Proxy.Invoker = pipeline;
                    return prx;
                }
            }

            private Connection? _cachedClientConnection;
            private Connection? _cachedServerConnection;
            private readonly Server _server;

            public async Task<(Connection, Connection)> AcceptAndConnectAsync()
            {
                Connection clientConnection;
                Connection serverConnection;

                if (Endpoint.Transport == "udp")
                {
                    serverConnection = new Connection(
                        Server.DefaultServerTransport.Listen(
                            Endpoint,
                            _server.ConnectionOptions,
                            LogAttributeLoggerFactory.Instance).Connection!,
                        _server.Dispatcher,
                        _server.ConnectionOptions,
                        LogAttributeLoggerFactory.Instance);

                    _ = serverConnection.ConnectAsync(default);
                    clientConnection = await ConnectAsync(serverConnection.LocalEndpoint!);
                }
                else
                {
                    using IListener listener = Server.DefaultServerTransport.Listen(
                        Endpoint,
                        _server.ConnectionOptions,
                        LogAttributeLoggerFactory.Instance).Listener!;
                    Task<Connection> serverTask = AcceptAsync(listener);
                    Task<Connection> clientTask = ConnectAsync(listener.Endpoint);
                    serverConnection = await serverTask;
                    clientConnection = await clientTask;
                }

                return (serverConnection, clientConnection);

                async Task<Connection> AcceptAsync(IListener listener)
                {
                    var connection = new Connection(await listener.AcceptAsync(),
                                                    _server.Dispatcher,
                                                    _server.ConnectionOptions,
                                                    LogAttributeLoggerFactory.Instance);
                    await connection.ConnectAsync(default);
                    return connection;
                }

                async Task<Connection> ConnectAsync(Endpoint endpoint)
                {
                    var connection = new Connection
                    {
                        RemoteEndpoint = endpoint,
                        Options = ClientConnectionOptions
                    };
                    await connection.ConnectAsync(default);
                    return connection;
                }
            }

            public async ValueTask DisposeAsync()
            {
                if (_cachedClientConnection != null)
                {
                    await _cachedClientConnection.DisposeAsync();
                    await _cachedServerConnection!.DisposeAsync();
                }
                await _server.DisposeAsync();
            }

            public ConnectionFactory(
                string transport = "coloc",
                Protocol protocol = Protocol.Ice2,
                bool secure = false,
                ClientConnectionOptions? clientConnectionOptions = null,
                ServerConnectionOptions? serverConnectionOptions = null,
                IDispatcher? dispatcher = null)
            {
                if (secure)
                {
                    clientConnectionOptions ??= new();
                    clientConnectionOptions.AuthenticationOptions = new()
                    {
                        RemoteCertificateValidationCallback =
                            CertificateValidaton.GetServerCertificateValidationCallback(
                                certificateAuthorities: new X509Certificate2Collection()
                                {
                                    new X509Certificate2("../../../certs/cacert.pem")
                                })
                    };

                    serverConnectionOptions ??= new();
                    serverConnectionOptions.AuthenticationOptions = new()
                    {
                        ClientCertificateRequired = false,
                        ServerCertificate = new X509Certificate2("../../../certs/server.p12", "password")
                    };
                }

                if (dispatcher != null)
                {
                    var router = new Router().UseLogger(LogAttributeLoggerFactory.Instance);
                    router.Mount("/", dispatcher);
                    dispatcher = router;
                }

                _server = new Server { ConnectionOptions = serverConnectionOptions ?? new(), Dispatcher = dispatcher };
                ClientConnectionOptions = clientConnectionOptions ?? new();

                if (transport == "coloc")
                {
                    Endpoint = new Endpoint(Protocol.Ice2,
                                            transport,
                                            Host: Guid.NewGuid().ToString(),
                                            Port: 4062,
                                            ImmutableList<EndpointParam>.Empty);
                }
                else if (transport == "udp" || protocol == Protocol.Ice1)
                {
                    if (secure)
                    {
                        if (transport == "tcp")
                        {
                            transport = "ssl";
                        }
                    }
                    Endpoint = $"{transport} -h 127.0.0.1";
                }
                else
                {
                    Endpoint = $"ice+{transport}://127.0.0.1:0?tls={(secure ? "true" : "false")}";
                }
            }
        }

        [TestCase(Protocol.Ice2, "tcp", false)]
        [TestCase(Protocol.Ice2, "tcp", true)]
        [TestCase(Protocol.Ice1, "tcp", false)]
        [TestCase(Protocol.Ice1, "tcp", true)]
        [TestCase(Protocol.Ice2, "coloc", false)]
        [TestCase(Protocol.Ice2, "coloc", true)]
        public async Task Connection_AbortAsync(Protocol protocol, string transport, bool closeClientSide)
        {
            using var semaphore = new SemaphoreSlim(0);
            await using var factory = new ConnectionFactory(
                transport,
                dispatcher: new InlineDispatcher(async (request, cancel) =>
                {
                    await semaphore.WaitAsync(cancel);
                    return new OutgoingResponse(request, Payload.FromVoidReturnValue(request));
                }),
                protocol: protocol);

            // Perform an invocation
            Task pingTask = factory.ServicePrx.IcePingAsync();

            if (closeClientSide)
            {
                await factory.ClientConnection.AbortAsync();
            }
            else
            {
                await factory.ServerConnection.AbortAsync();
            }
            Assert.ThrowsAsync<ConnectionLostException>(async () => await pingTask);
            semaphore.Release();
        }

        [TestCase(Protocol.Ice1, false)]
        [TestCase(Protocol.Ice1, true)]
        [TestCase(Protocol.Ice2, false)]
        [TestCase(Protocol.Ice2, true)]
        public async Task Connection_ClosedEventAsync(Protocol protocol, bool closeClientSide)
        {
            await using var factory = new ConnectionFactory("tcp", protocol);

            using var semaphore = new SemaphoreSlim(0);
            EventHandler<ClosedEventArgs> handler = (sender, args) =>
            {
                Assert.That(sender, Is.AssignableTo<Connection>());
                Assert.That(args.Exception, Is.AssignableTo<ConnectionClosedException>());
                semaphore.Release();
            };
            factory.ClientConnection.Closed += handler;
            factory.ServerConnection.Closed += handler;

            await (closeClientSide ? factory.ClientConnection : factory.ServerConnection).ShutdownAsync();

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

            using var semaphore = new SemaphoreSlim(0);
            factory.ClientConnection.Closed += (sender, args) => semaphore.Release();
            factory.ServerConnection.Closed += (sender, args) => semaphore.Release();
            await semaphore.WaitAsync();
            await semaphore.WaitAsync();
        }

        [TestCase(Protocol.Ice1)]
        [TestCase(Protocol.Ice2)]
        public async Task Connection_ConnectTimeoutAsyncXXXX(Protocol protocol)
        {
            await using var factory = new ConnectionFactory("tcp", protocol: protocol);

            IServerTransport transport = new TcpServerTransport(new TcpOptions { ListenerBackLog = 1 });
            using IListener listener = transport.Listen(
                factory.Endpoint,
                new ServerConnectionOptions(),
                LogAttributeLoggerFactory.Instance).Listener!;

            // TODO: add test once it's possible to create a connection directly. Right now, the connect timeout
            // is handled by the client connection factory.
        }

        [TestCase("tcp", false)]
        [TestCase("tcp", true)]
        [TestCase("udp", false)]
        public async Task Connection_InformationAsync(string transport, bool secure)
        {
            await using var factory = new ConnectionFactory(transport, secure: secure);

            Assert.That(factory.ClientConnection.IsSecure, Is.EqualTo(secure));
            Assert.That(factory.ServerConnection.IsSecure, Is.EqualTo(secure));

            Socket? clientSocket =
                (factory.ClientConnection.UnderlyingConnection as NetworkSocketConnection)?.NetworkSocket.Socket;
            Assert.That(clientSocket, Is.Not.Null);

            Socket? serverSocket =
                (factory.ServerConnection.UnderlyingConnection as NetworkSocketConnection)?.NetworkSocket.Socket;
            Assert.That(serverSocket, Is.Not.Null);

            Assert.That(clientSocket!.RemoteEndPoint, Is.Not.Null);
            Assert.That(clientSocket.LocalEndPoint, Is.Not.Null);

            Assert.That(serverSocket!.LocalEndPoint, Is.Not.Null);

            Assert.AreEqual("127.0.0.1", factory.ClientConnection.LocalEndpoint!.Host);
            Assert.AreEqual("127.0.0.1", factory.ClientConnection.RemoteEndpoint!.Host);
            Assert.That(factory.ClientConnection.RemoteEndpoint!.Port, Is.EqualTo(factory.ServerConnection.LocalEndpoint!.Port));
            if (transport == "udp")
            {
                Assert.That(serverSocket.RemoteEndPoint, Is.Null);
                Assert.Throws<InvalidOperationException>(() => _ = factory.ServerConnection.RemoteEndpoint);
            }
            else
            {
                Assert.That(serverSocket.RemoteEndPoint, Is.Not.Null);
                Assert.That(factory.ClientConnection.LocalEndpoint.Port, Is.EqualTo(factory.ServerConnection.RemoteEndpoint!.Port));
                Assert.AreEqual("127.0.0.1", factory.ClientConnection.RemoteEndpoint.Host);
            }
            Assert.That(factory.ClientConnection.IsServer, Is.False);
            Assert.That(factory.ServerConnection.IsServer, Is.True);

            Assert.AreEqual(factory.ClientConnection.RemoteEndpoint.Port, ((IPEndPoint)clientSocket.RemoteEndPoint!).Port);
            Assert.AreEqual(factory.ClientConnection.LocalEndpoint.Port, ((IPEndPoint)clientSocket.LocalEndPoint!).Port);

            Assert.AreEqual("127.0.0.1", ((IPEndPoint)clientSocket.LocalEndPoint).Address.ToString());
            Assert.AreEqual("127.0.0.1", ((IPEndPoint)clientSocket.RemoteEndPoint).Address.ToString());

            Assert.That($"{factory.ClientConnection}", Does.StartWith(factory.ClientConnection.UnderlyingConnection!.GetType().Name));
            Assert.That($"{factory.ServerConnection}", Does.StartWith(factory.ServerConnection.UnderlyingConnection!.GetType().Name));

            if (transport == "udp")
            {
                Assert.AreEqual(SocketType.Dgram, clientSocket.SocketType);
            }
            else if (transport == "tcp")
            {
                Assert.AreEqual(SocketType.Stream, clientSocket.SocketType);
            }

            if (secure)
            {
                Assert.AreEqual("tcp", transport);
                SslStream? clientSslStream =
                    (factory.ClientConnection.UnderlyingConnection as NetworkSocketConnection)?.NetworkSocket.SslStream;

                Assert.That(clientSslStream, Is.Not.Null);

                SslStream? serverSslStream =
                    (factory.ServerConnection.UnderlyingConnection as NetworkSocketConnection)?.NetworkSocket.SslStream;

                Assert.That(serverSslStream, Is.Not.Null);

                Assert.That(clientSslStream!.CheckCertRevocationStatus, Is.False);
                Assert.That(clientSslStream.IsEncrypted, Is.True);
                Assert.That(clientSslStream.IsMutuallyAuthenticated, Is.False);
                Assert.That(clientSslStream.IsSigned, Is.True);
                Assert.That(clientSslStream.LocalCertificate, Is.Null);

                if (OperatingSystem.IsMacOS())
                {
                    // APLN doesn't work on macOS (we keep this check to figure out when it will be supported)
                    Assert.That(clientSslStream.NegotiatedApplicationProtocol.ToString(), Is.Empty);
                    Assert.That(serverSslStream!.NegotiatedApplicationProtocol.ToString(), Is.Empty);
                }
                else
                {
                    Assert.That(clientSslStream.NegotiatedApplicationProtocol.ToString(),
                                Is.EqualTo(Protocol.Ice2.GetName()));
                    Assert.That(serverSslStream!.NegotiatedApplicationProtocol.ToString(),
                                Is.EqualTo(Protocol.Ice2.GetName()));
                }

                Assert.That(clientSslStream.RemoteCertificate, Is.Not.Null);
                Assert.That(serverSslStream.NegotiatedApplicationProtocol,
                            Is.EqualTo(clientSslStream.NegotiatedApplicationProtocol));
                Assert.That(serverSslStream.LocalCertificate, Is.Not.Null);
                Assert.That(serverSslStream.RemoteCertificate, Is.Null);
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

            Assert.That(factory.ClientConnection.IdleTimeout, Is.EqualTo(TimeSpan.FromSeconds(2)));

            if (protocol == Protocol.Ice1)
            {
                Assert.That(factory.ServerConnection.IdleTimeout, Is.EqualTo(TimeSpan.FromSeconds(3)));
            }
            else
            {
                Assert.That(factory.ServerConnection.IdleTimeout, Is.EqualTo(TimeSpan.FromSeconds(2)));
            }
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
            Assert.That(factory.ClientConnection.KeepAlive, Is.True);
            Assert.That(factory.ServerConnection.KeepAlive, Is.True);
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

            using var semaphore = new SemaphoreSlim(0);
            EventHandler handler = (sender, args) =>
            {
                Assert.That(sender, Is.EqualTo(heartbeatOnClient ? factory.ServerConnection : factory.ClientConnection));
                semaphore.Release();
            };
            if (heartbeatOnClient)
            {
                factory.ClientConnection.PingReceived += (sender, args) => Assert.Fail();
                factory.ServerConnection.PingReceived += handler;
            }
            else
            {
                factory.ClientConnection.PingReceived += handler;
                factory.ServerConnection.PingReceived += (sender, args) => Assert.Fail();
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
                serverConnectionOptions: new() { IdleTimeout = TimeSpan.FromMilliseconds(1000) },
                dispatcher: new InlineDispatcher(async (request, cancel) =>
                {
                    await dispatchSemaphore.WaitAsync(cancel);
                    return new OutgoingResponse(request, Payload.FromVoidReturnValue(request));
                }));

            // Perform an invocation
            Task pingTask = factory.ServicePrx.IcePingAsync();

            // Make sure we receive few pings while the invocation is pending.
            using var semaphore = new SemaphoreSlim(0);
            factory.ClientConnection.PingReceived += (sender, args) => semaphore.Release();
            await semaphore.WaitAsync();
            await semaphore.WaitAsync();

            dispatchSemaphore.Release();

            await pingTask;
        }

        [TestCase(Protocol.Ice2, "tcp", false)]
        [TestCase(Protocol.Ice2, "tcp", true)]
        [TestCase(Protocol.Ice1, "tcp", false)]
        [TestCase(Protocol.Ice1, "tcp", true)]
        [TestCase(Protocol.Ice2, "coloc", false)]
        [TestCase(Protocol.Ice2, "coloc", true)]
        public async Task Connection_ShutdownAsync(Protocol protocol, string transport, bool closeClientSide)
        {
            using var waitForDispatchSemaphore = new SemaphoreSlim(0);
            using var dispatchSemaphore = new SemaphoreSlim(0);
            await using var factory = new ConnectionFactory(
                transport,
                protocol,
                dispatcher: new InlineDispatcher(async (request, cancel) =>
                {
                    waitForDispatchSemaphore.Release();
                    await dispatchSemaphore.WaitAsync(cancel);
                    return new OutgoingResponse(request, Payload.FromVoidReturnValue(request));
                }));

            // Perform an invocation.
            Task pingTask = factory.ServicePrx.IcePingAsync();
            await waitForDispatchSemaphore.WaitAsync();

            // Shutdown the connection.
            Task shutdownTask = (closeClientSide ? factory.ClientConnection : factory.ServerConnection).ShutdownAsync("message");
            Assert.That(dispatchSemaphore.Release(), Is.EqualTo(0));
            await shutdownTask;

            if (protocol == Protocol.Ice1 && closeClientSide)
            {
                // With Ice1, when closing the connection with a pending invocation, invocations are aborted
                // immediately. The Ice1 protocol doesn't support reliably waiting for the response.
                Assert.ThrowsAsync<ConnectionClosedException>(
                    async () => await factory.ServicePrx.IcePingAsync());
            }
            else
            {
                // Ensure the invocation is successful.
                Assert.DoesNotThrowAsync(async () => await pingTask);
            }

            // Next invocation on the connection should throw ConnectionClosedException.
            Assert.ThrowsAsync<ConnectionClosedException>(
                async () => await factory.ServicePrx.IcePingAsync());
        }

        [TestCase(false, Protocol.Ice1)]
        [TestCase(true, Protocol.Ice1)]
        [TestCase(false, Protocol.Ice2)]
        [TestCase(true, Protocol.Ice2)]
        public async Task Connection_ShutdownCancellationAsync(bool closeClientSide, Protocol protocol)
        {
            using var waitForDispatchSemaphore = new SemaphoreSlim(0);
            using var dispatchSemaphore = new SemaphoreSlim(0);
            await using var factory = new ConnectionFactory(
                "tcp",
                protocol,
                dispatcher: new InlineDispatcher(async (request, cancel) =>
                {
                    waitForDispatchSemaphore.Release();
                    try
                    {
                        await Task.Delay(-1, cancel);
                    }
                    catch (OperationCanceledException)
                    {
                        dispatchSemaphore.Release();
                        throw;
                    }
                    catch
                    {
                    }
                    Assert.Fail();
                    return new OutgoingResponse(request, Payload.FromVoidReturnValue(request));
                }));

            // Perform an invocation
            Task pingTask = factory.ServicePrx.IcePingAsync();
            await waitForDispatchSemaphore.WaitAsync();

            using var cancelSource = new CancellationTokenSource();
            if (closeClientSide)
            {
                Task shutdownTask = factory.ClientConnection.ShutdownAsync("client message", cancelSource.Token);
                cancelSource.Cancel();

                // Ensure that dispatch is canceled (with Ice1 it's canceled on receive of the CloseConnection
                // frame and the GoAwayCanceled frame for Ice2.
                dispatchSemaphore.Wait();

                // The invocation on the connection has been canceled by the shutdown cancellation
                var ex = Assert.ThrowsAsync<OperationCanceledException>(async () => await pingTask);

                if (protocol == Protocol.Ice1)
                {
                    // Client-side Ice1 invocations are canceled immediately on shutdown.
                    Assert.That(ex!.Message, Is.EqualTo("connection shutdown"));
                }
                else
                {
                    // Client-side Ice2 invocations are canceled when the dispatch is canceled by the peer.
                    Assert.That(ex!.Message, Is.EqualTo("dispatch canceled by peer"));
                }
            }
            else
            {
                Task shutdownTask = factory.ServerConnection.ShutdownAsync("server message", cancelSource.Token);
                Assert.That(shutdownTask.IsCompleted, Is.False);
                cancelSource.Cancel();

                // Ensure the dispatch is canceled.
                dispatchSemaphore.Wait();

                // The invocation on the connection should throw a DispatchException
                if (protocol == Protocol.Ice1)
                {
                    DispatchException? ex = Assert.ThrowsAsync<DispatchException>(async () => await pingTask);
                    Assert.That(ex!.RetryPolicy, Is.EqualTo(RetryPolicy.NoRetry));
                }
                else
                {
                    var ex = Assert.ThrowsAsync<OperationCanceledException>(async () => await pingTask);
                    Assert.That(ex!.Message, Is.EqualTo("dispatch canceled by peer"));
                }
            }
        }

        [TestCase(Protocol.Ice2, "tcp", false)]
        [TestCase(Protocol.Ice2, "tcp", true)]
        [TestCase(Protocol.Ice1, "tcp", false)]
        [TestCase(Protocol.Ice1, "tcp", true)]
        [TestCase(Protocol.Ice2, "coloc", false)]
        [TestCase(Protocol.Ice2, "coloc", true)]
        public async Task Connection_ShutdownAsync_CloseTimeoutAsync(
            Protocol protocol,
            string transport,
            bool closeClientSide)
        {
            using var semaphore = new SemaphoreSlim(0);
            using var waitForDispatchSemaphore = new SemaphoreSlim(0);
            await using var factory = new ConnectionFactory(
                transport,
                protocol: protocol,
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
                    return new OutgoingResponse(request, Payload.FromVoidReturnValue(request));
                }));

            // Perform an invocation
            Task pingTask = factory.ServicePrx.IcePingAsync();
            await waitForDispatchSemaphore.WaitAsync();

            if (closeClientSide)
            {
                // Shutdown should trigger the abort of the connection after the close timeout
                await factory.ClientConnection.ShutdownAsync();
                if (protocol == Protocol.Ice1)
                {
                    // Invocations are canceled immediately on shutdown with Ice1
                    Assert.ThrowsAsync<OperationCanceledException>(async () => await pingTask);
                }
                else
                {
                    Assert.ThrowsAsync<ConnectionLostException>(async () => await pingTask);
                }
            }
            else
            {
                // Shutdown should trigger the abort of the connection after the close timeout
                await factory.ServerConnection.ShutdownAsync();
                Assert.ThrowsAsync<ConnectionLostException>(async () => await pingTask);
            }

            semaphore.Release();
        }
    }
}
