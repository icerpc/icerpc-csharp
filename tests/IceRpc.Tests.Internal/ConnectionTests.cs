// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Transports;
using NUnit.Framework;
using System.Collections.Immutable;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

namespace IceRpc.Tests.Internal
{
    [Parallelizable(ParallelScope.All)]
    [Timeout(5000)]
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
            private readonly SslClientAuthenticationOptions? _clientAuthenticationOptions;
            private readonly ConnectionOptions _clientConnectionOptions;
            private readonly object? _clientTransportOptions;
            private readonly IDispatcher? _dispatcher;
            private readonly SslServerAuthenticationOptions? _serverAuthenticationOptions;
            private readonly ConnectionOptions _serverConnectionOptions;
            private readonly object? _serverTransportOptions;

            public async Task<(Connection, Connection)> AcceptAndConnectAsync()
            {
                IServerTransport serverTransport = TestHelper.CreateServerTransport(
                    Endpoint,
                    options: _serverTransportOptions,
                    authenticationOptions: _serverAuthenticationOptions);

                Connection clientConnection;
                Connection serverConnection;
                if (Endpoint.Transport == "udp")
                {
                    serverConnection = new Connection(
                        serverTransport.Listen(Endpoint, LogAttributeLoggerFactory.Instance).Connection!,
                        _dispatcher,
                        _serverConnectionOptions,
                        LogAttributeLoggerFactory.Instance);
                    await serverConnection.ConnectAsync(default);
                    clientConnection = await ConnectAsync(serverConnection.LocalEndpoint!);
                }
                else
                {
                    using IListener listener = serverTransport.Listen(
                        Endpoint,
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
                                                    _dispatcher,
                                                    _serverConnectionOptions,
                                                    LogAttributeLoggerFactory.Instance);
                    await connection.ConnectAsync(default);
                    return connection;
                }

                async Task<Connection> ConnectAsync(Endpoint endpoint)
                {
                    var connection = new Connection(_clientConnectionOptions)
                    {
                        RemoteEndpoint = endpoint,
                        ClientTransport = TestHelper.CreateClientTransport(
                            endpoint,
                            options: _clientTransportOptions,
                            authenticationOptions: _clientAuthenticationOptions),
                        LoggerFactory = LogAttributeLoggerFactory.Instance
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
            }

            public ConnectionFactory(
                string transport = "coloc",
                Protocol protocol = Protocol.Ice2,
                bool secure = false,
                ConnectionOptions? clientConnectionOptions = null,
                ConnectionOptions? serverConnectionOptions = null,
                object? clientTransportOptions = null,
                object? serverTransportOptions = null,
                IDispatcher? dispatcher = null)
            {
                _clientConnectionOptions = clientConnectionOptions ?? new();
                _clientTransportOptions = clientTransportOptions;
                _serverConnectionOptions = serverConnectionOptions ?? new();
                _serverTransportOptions = serverTransportOptions;
                if (secure)
                {
                    _clientAuthenticationOptions = new()
                    {
                        RemoteCertificateValidationCallback =
                            CertificateValidaton.GetServerCertificateValidationCallback(
                                certificateAuthorities: new X509Certificate2Collection()
                                {
                                    new X509Certificate2("../../../certs/cacert.pem")
                                })
                    };

                    _serverAuthenticationOptions = new()
                    {
                        ClientCertificateRequired = false,
                        ServerCertificate = new X509Certificate2("../../../certs/server.p12", "password")
                    };
                }

                if (transport == "coloc")
                {
                    Endpoint = new Endpoint(Protocol.Ice2,
                                            transport,
                                            host: Guid.NewGuid().ToString(),
                                            port: 4062,
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

                if (dispatcher != null)
                {
                    Router router = new Router().UseLogger(LogAttributeLoggerFactory.Instance);
                    router.Mount("/", dispatcher);
                    _dispatcher = router;
                }
            }
        }

        [TestCase(Protocol.Ice2, "tcp", false)]
        [TestCase(Protocol.Ice2, "tcp", true)]
        [TestCase(Protocol.Ice1, "tcp", false)]
        [TestCase(Protocol.Ice1, "tcp", true)]
        [TestCase(Protocol.Ice2, "coloc", false)]
        [TestCase(Protocol.Ice2, "coloc", true)]
        [TestCase(Protocol.Ice1, "coloc", false)]
        [TestCase(Protocol.Ice1, "coloc", true)]
        public async Task Connection_CloseAsync(Protocol protocol, string transport, bool closeClientSide)
        {
            using var semaphore = new SemaphoreSlim(0);
            await using var factory = new ConnectionFactory(
                transport,
                dispatcher: new InlineDispatcher(async (request, cancel) =>
                {
                    await semaphore.WaitAsync(cancel);
                    return OutgoingResponse.ForPayload(request, default);
                }),
                protocol: protocol);

            // Perform an invocation
            Task pingTask = factory.ServicePrx.IcePingAsync();

            if (closeClientSide)
            {
                await factory.ClientConnection.CloseAsync();
            }
            else
            {
                await factory.ServerConnection.CloseAsync();
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
                clientTransportOptions: new TcpOptions()
                {
                    IdleTimeout = idleOnClient ? TimeSpan.FromMilliseconds(500) : TimeSpan.FromHours(1)
                },
                serverTransportOptions: new TcpOptions()
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
        public async Task Connection_ConnectTimeoutAsync(Protocol protocol)
        {
            await using var factory = new ConnectionFactory("tcp", protocol: protocol);

            IServerTransport transport = new TcpServerTransport(new TcpOptions { ListenerBackLog = 1 }, new(), null);
            using IListener listener = transport.Listen(factory.Endpoint, LogAttributeLoggerFactory.Instance).Listener!;

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

            var clientSocket = (NetworkSocket)factory.ClientConnection.GetNetworkSocket()!;
            Assert.That(clientSocket, Is.Not.Null);

            var serverSocket = (NetworkSocket)factory.ServerConnection.GetNetworkSocket()!;
            Assert.That(serverSocket, Is.Not.Null);

            Assert.That(clientSocket.Socket!.RemoteEndPoint, Is.Not.Null);
            Assert.That(clientSocket.Socket!.LocalEndPoint, Is.Not.Null);

            Assert.That(serverSocket.Socket!.LocalEndPoint, Is.Not.Null);

            Assert.AreEqual("127.0.0.1", factory.ClientConnection.LocalEndpoint!.Host);
            Assert.AreEqual("127.0.0.1", factory.ClientConnection.RemoteEndpoint!.Host);
            Assert.That(factory.ClientConnection.RemoteEndpoint!.Port,
                        Is.EqualTo(factory.ServerConnection.LocalEndpoint!.Port));
            if (transport == "udp")
            {
                Assert.That(serverSocket.Socket!.RemoteEndPoint, Is.Null);
                Assert.That(factory.ServerConnection.RemoteEndpoint, Is.Null);
            }
            else
            {
                Assert.That(serverSocket.Socket!.RemoteEndPoint, Is.Not.Null);
                Assert.That(factory.ClientConnection.LocalEndpoint.Port,
                            Is.EqualTo(factory.ServerConnection.RemoteEndpoint!.Port));
                Assert.AreEqual("127.0.0.1", factory.ClientConnection.RemoteEndpoint.Host);
            }
            Assert.That(factory.ClientConnection.IsServer, Is.False);
            Assert.That(factory.ServerConnection.IsServer, Is.True);

            Assert.AreEqual(factory.ClientConnection.RemoteEndpoint.Port,
                            ((IPEndPoint)clientSocket.Socket!.RemoteEndPoint!).Port);
            Assert.AreEqual(factory.ClientConnection.LocalEndpoint.Port,
                            ((IPEndPoint)clientSocket.Socket!.LocalEndPoint!).Port);

            Assert.AreEqual("127.0.0.1", ((IPEndPoint)clientSocket.Socket!.LocalEndPoint).Address.ToString());
            Assert.AreEqual("127.0.0.1", ((IPEndPoint)clientSocket.Socket!.RemoteEndPoint).Address.ToString());

            Assert.That($"{factory.ClientConnection}", Does.StartWith(clientSocket.GetType().Name));
            Assert.That($"{factory.ServerConnection}", Does.StartWith(serverSocket.GetType().Name));

            if (transport == "udp")
            {
                Assert.AreEqual(SocketType.Dgram, clientSocket.Socket!.SocketType);
            }
            else if (transport == "tcp")
            {
                Assert.AreEqual(SocketType.Stream, clientSocket.Socket!.SocketType);
            }

            if (secure)
            {
                Assert.AreEqual("tcp", transport);

                SslStream? clientSslStream = factory.ClientConnection!.GetNetworkSocket()!.SslStream;
                Assert.That(clientSslStream, Is.Not.Null);

                SslStream? serverSslStream = factory.ServerConnection!.GetNetworkSocket()!.SslStream;
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
                clientTransportOptions: new TcpOptions()
                {
                    IdleTimeout = TimeSpan.FromSeconds(2)
                },
                serverTransportOptions: new TcpOptions()
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
                clientTransportOptions: new TcpOptions()
                {
                    IdleTimeout = TimeSpan.FromMilliseconds(500),
                },
                clientConnectionOptions: new()
                {
                    KeepAlive = heartbeatOnClient
                },
                serverTransportOptions: new TcpOptions()
                {
                    IdleTimeout = TimeSpan.FromMilliseconds(500),
                },
                serverConnectionOptions: new()
                {
                    KeepAlive = !heartbeatOnClient
                });

            await Task.Delay(TimeSpan.FromSeconds(2));

            Assert.That(factory.ClientConnection.State, Is.EqualTo(ConnectionState.Active));
            Assert.That(factory.ServerConnection.State, Is.EqualTo(ConnectionState.Active));
        }

        [TestCase(Protocol.Ice1)]
        [TestCase(Protocol.Ice2)]
        public async Task Connection_KeepAliveOnInvocationAsync(Protocol protocol)
        {
            using var dispatchSemaphore = new SemaphoreSlim(0);
            await using var factory = new ConnectionFactory(
                "tcp",
                protocol,
                serverTransportOptions: new TcpOptions() { IdleTimeout = TimeSpan.FromSeconds(1) },
                dispatcher: new InlineDispatcher(async (request, cancel) =>
                {
                    await dispatchSemaphore.WaitAsync(cancel);
                    return OutgoingResponse.ForPayload(request, default);
                }));

            // Perform an invocation and wait 2 seconds. The connection shouldn't close.
            Task pingTask = factory.ServicePrx.IcePingAsync();
            await Task.Delay(TimeSpan.FromSeconds(2));
            Assert.That(factory.ClientConnection.State, Is.EqualTo(ConnectionState.Active));
            Assert.That(factory.ServerConnection.State, Is.EqualTo(ConnectionState.Active));
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
                    return OutgoingResponse.ForPayload(request, default);
                }));

            // Perform an invocation.
            Task pingTask = factory.ServicePrx.IcePingAsync();
            await waitForDispatchSemaphore.WaitAsync();

            // Shutdown the connection.
            Task shutdownTask =
                (closeClientSide ? factory.ClientConnection : factory.ServerConnection).ShutdownAsync("message");
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
        // [TestCase(true, Protocol.Ice1)]
        // [TestCase(false, Protocol.Ice2)]
        // [TestCase(true, Protocol.Ice2)]
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
                    return OutgoingResponse.ForPayload(request, default);
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
                // frame and the GoAwayCanceled frame for Ice2).
                dispatchSemaphore.Wait();

                // The invocation on the connection has been canceled by the shutdown cancellation
                Exception? ex = Assert.ThrowsAsync<OperationCanceledException>(async () => await pingTask);

                if (protocol == Protocol.Ice1)
                {
                    // Client-side Ice1 invocations are canceled immediately on shutdown.
                    Assert.That(ex!.Message, Is.EqualTo("client message"));
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
                    Exception? ex = Assert.ThrowsAsync<OperationCanceledException>(async () => await pingTask);
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
                    return OutgoingResponse.ForPayload(request, default);
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
