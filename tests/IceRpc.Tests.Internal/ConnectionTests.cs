// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Internal;
using IceRpc.Transports;
using NUnit.Framework;
using System.Collections.Immutable;
using System.Net.Security;
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
            private readonly ConnectionOptions _clientConnectionOptions;
            private readonly object? _clientTransportOptions;
            private readonly IDispatcher? _dispatcher;
            private readonly ConnectionOptions _serverConnectionOptions;
            private readonly object? _serverTransportOptions;

            public Task<(Connection, Connection)> AcceptAndConnectAsync()
            {
                return Endpoint.Protocol == Protocol.Ice1 ?
                    PerformAcceptAndConnectAsync(
                        TestHelper.CreateSimpleServerTransport(
                            Endpoint.Transport,
                            options: _serverTransportOptions),
                            Connection.CreateProtocolConnectionAsync) :
                    PerformAcceptAndConnectAsync(
                        TestHelper.CreateMultiplexedServerTransport(
                            Endpoint.Transport,
                            options: _serverTransportOptions as TcpServerOptions),
                        Connection.CreateProtocolConnectionAsync);

                async Task<(Connection, Connection)> PerformAcceptAndConnectAsync<T>(
                    IServerTransport<T> serverTransport,
                    ProtocolConnectionFactory<T> protocolConnectionFactory) where T : INetworkConnection
                {
                    using IListener<T> listener =
                        serverTransport.Listen(Endpoint, LogAttributeLoggerFactory.Instance);
                    Task<Connection> serverTask = AcceptAsync(listener, protocolConnectionFactory);
                    Task<Connection> clientTask = ConnectAsync(listener.Endpoint);
                    return (await serverTask, await clientTask);
                }

                async Task<Connection> AcceptAsync<T>(
                    IListener<T> listener,
                    ProtocolConnectionFactory<T> protocolConnectionFactory) where T : INetworkConnection
                {
                    T networkConnection = await listener.AcceptAsync();

                    var connection = new Connection(networkConnection, Endpoint.Protocol)
                    {
                        Dispatcher = _dispatcher,
                        Options = _serverConnectionOptions
                    };
                    await connection.ConnectAsync<T>(networkConnection,
                                                     protocolConnectionFactory,
                                                     closedEventHandler: null);
                    return connection;
                }

                async Task<Connection> ConnectAsync(Endpoint endpoint)
                {
                    var connection = endpoint.Protocol == Protocol.Ice1 ?
                        new Connection
                        {
                            SimpleClientTransport = TestHelper.CreateSimpleClientTransport(
                                endpoint.Transport,
                                options: _clientTransportOptions),
                            Options = _clientConnectionOptions,
                            RemoteEndpoint = endpoint
                        } :
                        new Connection
                        {
                            MultiplexedClientTransport = TestHelper.CreateMultiplexedClientTransport(
                                endpoint.Transport,
                                options: _clientTransportOptions as TcpClientOptions),
                            Options = _clientConnectionOptions,
                            RemoteEndpoint = endpoint
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
                ProtocolCode protocol = ProtocolCode.Ice2,
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
                    _clientTransportOptions ??= new TcpClientOptions();
                    var tcpClientOptions = (TcpClientOptions)_clientTransportOptions;

                    tcpClientOptions.AuthenticationOptions = new()
                    {
                        RemoteCertificateValidationCallback =
                            CertificateValidaton.GetServerCertificateValidationCallback(
                                certificateAuthorities: new X509Certificate2Collection()
                                {
                                    new X509Certificate2("../../../certs/cacert.pem")
                                })
                    };

                    _serverTransportOptions ??= new TcpServerOptions();
                    var tcpServerOptions = (TcpServerOptions)_serverTransportOptions;

                    tcpServerOptions.AuthenticationOptions = new()
                    {
                        ClientCertificateRequired = false,
                        ServerCertificate = new X509Certificate2("../../../certs/server.p12", "password")
                    };
                }

                if (transport == "coloc")
                {
                    Endpoint = new Endpoint(Protocol.FromProtocolCode(protocol),
                                            transport,
                                            host: Guid.NewGuid().ToString(),
                                            port: 4062,
                                            ImmutableList<EndpointParam>.Empty);
                }
                else if (transport == "udp" || protocol == ProtocolCode.Ice1)
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

        [TestCase(ProtocolCode.Ice2, "tcp", false)]
        [TestCase(ProtocolCode.Ice2, "tcp", true)]
        [TestCase(ProtocolCode.Ice1, "tcp", false)]
        [TestCase(ProtocolCode.Ice1, "tcp", true)]
        [TestCase(ProtocolCode.Ice2, "coloc", false)]
        [TestCase(ProtocolCode.Ice2, "coloc", true)]
        [TestCase(ProtocolCode.Ice1, "coloc", false)]
        [TestCase(ProtocolCode.Ice1, "coloc", true)]
        public async Task Connection_CloseAsync(ProtocolCode protocol, string transport, bool closeClientSide)
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

        [TestCase(ProtocolCode.Ice1, false)]
        [TestCase(ProtocolCode.Ice1, true)]
        [TestCase(ProtocolCode.Ice2, false)]
        [TestCase(ProtocolCode.Ice2, true)]
        public async Task Connection_ClosedEventAsync(ProtocolCode protocol, bool closeClientSide)
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

        [TestCase(ProtocolCode.Ice1, false)]
        [TestCase(ProtocolCode.Ice1, true)]
        [TestCase(ProtocolCode.Ice2, false)]
        [TestCase(ProtocolCode.Ice2, true)]
        public async Task Connection_CloseOnIdleAsync(ProtocolCode protocol, bool idleOnClient)
        {
            await using var factory = new ConnectionFactory(
                "tcp",
                protocol,
                clientTransportOptions: new TcpClientOptions()
                {
                    IdleTimeout = idleOnClient ? TimeSpan.FromMilliseconds(500) : TimeSpan.FromHours(1)
                },
                serverTransportOptions: new TcpServerOptions()
                {
                    IdleTimeout = idleOnClient ? TimeSpan.FromHours(1) : TimeSpan.FromMilliseconds(500)
                });

            using var semaphore = new SemaphoreSlim(0);
            factory.ClientConnection.Closed += (sender, args) => semaphore.Release();
            factory.ServerConnection.Closed += (sender, args) => semaphore.Release();
            await semaphore.WaitAsync();
            await semaphore.WaitAsync();
        }

        [TestCase(ProtocolCode.Ice1)]
        [TestCase(ProtocolCode.Ice2)]
        public async Task Connection_ConnectTimeoutAsync(ProtocolCode protocol)
        {
            Endpoint endpoint = TestHelper.GetTestEndpoint(
                transport: "tcp",
                protocol: Protocol.FromProtocolCode(protocol));

            IServerTransport<ISimpleNetworkConnection> tcpServerTransport =
                new TcpServerTransport(new TcpServerOptions { ListenerBackLog = 1 });

            using IListener listener = protocol == ProtocolCode.Ice1 ?
                tcpServerTransport.Listen(endpoint, LogAttributeLoggerFactory.Instance) :
                (new SlicServerTransport(tcpServerTransport) as IServerTransport<IMultiplexedNetworkConnection>).
                    Listen(endpoint, LogAttributeLoggerFactory.Instance);

            await using var connection = new Connection
            {
                Options = new() { ConnectTimeout = TimeSpan.FromMilliseconds(100) },
                RemoteEndpoint = listener.Endpoint,
            };

            Assert.ThrowsAsync<ConnectTimeoutException>(() => connection.ConnectAsync(default));
        }

        [TestCase("tcp", false)]
        [TestCase("tcp", true)]
        [TestCase("udp", false)]
        public async Task Connection_InformationAsync(string transport, bool secure)
        {
            await using var factory = new ConnectionFactory(transport, protocol: ProtocolCode.Ice1, secure: secure);

            Assert.That(factory.ClientConnection.IsSecure, Is.EqualTo(secure));
            Assert.That(factory.ServerConnection.IsSecure, Is.EqualTo(secure));

            NetworkConnectionInformation? clientInformation = factory.ClientConnection.NetworkConnectionInformation;
            Assert.That(clientInformation, Is.Not.Null);

            NetworkConnectionInformation? serverInformation = factory.ServerConnection.NetworkConnectionInformation;
            Assert.That(serverInformation, Is.Not.Null);

            Assert.AreEqual("127.0.0.1", clientInformation?.LocalEndpoint.Host);
            Assert.AreEqual("127.0.0.1", clientInformation?.RemoteEndpoint.Host);
            Assert.That(clientInformation?.RemoteEndpoint!.Port, Is.EqualTo(serverInformation?.LocalEndpoint!.Port));
            if (transport != "udp")
            {
                Assert.That(clientInformation?.LocalEndpoint!.Port, Is.EqualTo(serverInformation?.RemoteEndpoint!.Port));
                Assert.AreEqual("127.0.0.1", clientInformation?.RemoteEndpoint!.Host);
            }
            Assert.That(factory.ClientConnection.IsServer, Is.False);
            Assert.That(factory.ServerConnection.IsServer, Is.True);

            if (secure)
            {
                Assert.AreEqual("tcp", transport);
                Assert.That(clientInformation?.RemoteCertificate, Is.Not.Null);
                Assert.That(serverInformation?.RemoteCertificate, Is.Null);
            }
        }

        [TestCase(ProtocolCode.Ice1)]
        [TestCase(ProtocolCode.Ice2)]
        public async Task Connection_IdleTimeoutAsync(ProtocolCode protocol)
        {
            await using var factory = new ConnectionFactory(
                "tcp",
                protocol: protocol,
                clientTransportOptions: new TcpClientOptions()
                {
                    IdleTimeout = TimeSpan.FromSeconds(2)
                },
                serverTransportOptions: new TcpServerOptions()
                {
                    IdleTimeout = TimeSpan.FromSeconds(3)
                });

            Assert.That(factory.ClientConnection.NetworkConnectionInformation?.IdleTimeout,
                        Is.EqualTo(TimeSpan.FromSeconds(2)));

            if (protocol == ProtocolCode.Ice1)
            {
                Assert.That(factory.ServerConnection.NetworkConnectionInformation?.IdleTimeout,
                            Is.EqualTo(TimeSpan.FromSeconds(3)));
            }
            else
            {
                Assert.That(factory.ServerConnection.NetworkConnectionInformation?.IdleTimeout,
                            Is.EqualTo(TimeSpan.FromSeconds(2)));
            }
        }

        [TestCase(ProtocolCode.Ice1)]
        [TestCase(ProtocolCode.Ice2)]
        public async Task Connection_KeepAliveAsync(ProtocolCode protocol)
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
            Assert.That(factory.ClientConnection.Options.KeepAlive, Is.True);
            Assert.That(factory.ServerConnection.Options.KeepAlive, Is.True);
        }

        [TestCase(ProtocolCode.Ice1, false)]
        [TestCase(ProtocolCode.Ice1, true)]
        [TestCase(ProtocolCode.Ice2, false)]
        [TestCase(ProtocolCode.Ice2, true)]
        public async Task Connection_KeepAliveOnIdleAsync(ProtocolCode protocol, bool heartbeatOnClient)
        {
            await using var factory = new ConnectionFactory(
                "tcp",
                protocol,
                clientTransportOptions: new TcpClientOptions()
                {
                    IdleTimeout = TimeSpan.FromMilliseconds(500),
                },
                clientConnectionOptions: new()
                {
                    KeepAlive = heartbeatOnClient
                },
                serverTransportOptions: new TcpServerOptions()
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

        [TestCase(ProtocolCode.Ice1)]
        [TestCase(ProtocolCode.Ice2)]
        public async Task Connection_KeepAliveOnInvocationAsync(ProtocolCode protocol)
        {
            using var dispatchSemaphore = new SemaphoreSlim(0);
            await using var factory = new ConnectionFactory(
                "tcp",
                protocol,
                serverTransportOptions: new TcpServerOptions() { IdleTimeout = TimeSpan.FromSeconds(1) },
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

        [TestCase(ProtocolCode.Ice2, "tcp", false)]
        [TestCase(ProtocolCode.Ice2, "tcp", true)]
        [TestCase(ProtocolCode.Ice1, "tcp", false)]
        [TestCase(ProtocolCode.Ice1, "tcp", true)]
        [TestCase(ProtocolCode.Ice2, "coloc", false)]
        [TestCase(ProtocolCode.Ice2, "coloc", true)]
        public async Task Connection_ShutdownAsync(ProtocolCode protocol, string transport, bool closeClientSide)
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

            if (protocol == ProtocolCode.Ice1 && closeClientSide)
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

        [TestCase(false, ProtocolCode.Ice1)]
        [TestCase(true, ProtocolCode.Ice1)]
        [TestCase(false, ProtocolCode.Ice2)]
        [TestCase(true, ProtocolCode.Ice2)]
        public async Task Connection_ShutdownCancellationAsync(bool closeClientSide, ProtocolCode protocol)
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

                // Ensure that dispatch is canceled.
                await dispatchSemaphore.WaitAsync();

                // The invocation on the connection has been canceled by the shutdown cancellation
                Assert.ThrowsAsync<OperationCanceledException>(async () => await pingTask);
            }
            else
            {
                Task shutdownTask = factory.ServerConnection.ShutdownAsync("server message", cancelSource.Token);
                Assert.That(shutdownTask.IsCompleted, Is.False);
                cancelSource.Cancel();

                // Ensure the dispatch is canceled.
                await dispatchSemaphore.WaitAsync();

                // The invocation on the connection should throw a DispatchException
                if (protocol == ProtocolCode.Ice1)
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

        [TestCase(ProtocolCode.Ice2, "tcp", false)]
        [TestCase(ProtocolCode.Ice2, "tcp", true)]
        [TestCase(ProtocolCode.Ice1, "tcp", false)]
        [TestCase(ProtocolCode.Ice1, "tcp", true)]
        [TestCase(ProtocolCode.Ice2, "coloc", false)]
        [TestCase(ProtocolCode.Ice2, "coloc", true)]
        public async Task Connection_ShutdownAsync_CloseTimeoutAsync(
            ProtocolCode protocol,
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
                if (protocol == ProtocolCode.Ice1)
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
