// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using System;
using System.Collections.Generic;
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

            public OutgoingConnectionOptions ClientConnectionOptions { get; }

            public Endpoint Endpoint { get; }

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

            public ILogger Logger => _server.Logger;

            public IServicePrx Proxy
            {
                get
                {
                    var proxy = IServicePrx.FromConnection(Client);
                    var pipeline = new Pipeline();
                    pipeline.Use(Interceptors.Logger(Runtime.DefaultLoggerFactory));
                    proxy.Invoker = pipeline;
                    return proxy;
                }
            }

            private Connection? _cachedClientConnection;
            private Connection? _cachedServerConnection;
            private readonly Server _server;

            public async Task<(Connection, Connection)> AcceptAndConnectAsync()
            {
                Connection clientConnection;
                Connection serverConnection;

                if (Endpoint.IsDatagram)
                {
                    serverConnection = new Connection(
                        Endpoint.CreateServerSocket(_server.ConnectionOptions, _server.Logger),
                        _server);
                    _ = serverConnection.ConnectAsync(default);
                    clientConnection = await ConnectAsync(serverConnection.LocalEndpoint!);
                }
                else
                {
                    using IAcceptor acceptor = Endpoint.CreateAcceptor(_server.ConnectionOptions, _server.Logger);
                    Task<Connection> serverTask = AcceptAsync(acceptor);
                    Task<Connection> clientTask = ConnectAsync(acceptor.Endpoint);
                    serverConnection = await serverTask;
                    clientConnection = await clientTask;
                }

                return (serverConnection, clientConnection);

                async Task<Connection> AcceptAsync(IAcceptor acceptor)
                {
                    var connection = new Connection(await acceptor.AcceptAsync(), _server);
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
                OutgoingConnectionOptions? clientConnectionOptions = null,
                IncomingConnectionOptions? serverConnectionOptions = null,
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

                IDispatcher? pipeline = null;
                if (dispatcher != null)
                {
                    pipeline = Middleware.Logger(Runtime.DefaultLoggerFactory)(dispatcher);
                }

                _server = new Server { ConnectionOptions = serverConnectionOptions ?? new(), Dispatcher = pipeline };
                ClientConnectionOptions = clientConnectionOptions ?? new();

                if (transport == "coloc")
                {
                    Endpoint = new ColocEndpoint(Guid.NewGuid().ToString(), 4062, protocol);
                }
                else if (transport == "udp" || protocol == Protocol.Ice1)
                {
                    if (secure)
                    {
                        if (transport == "tcp")
                        {
                            transport = "ssl";
                        }
                        else if (transport == "ws")
                        {
                            transport = "wss";
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
        [TestCase(Protocol.Ice2, "ws", false)]
        [TestCase(Protocol.Ice2, "ws", true)]
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
            Task pingTask = factory.Proxy.IcePingAsync();

            if (closeClientSide)
            {
                await factory.Client.AbortAsync();
            }
            else
            {
                await factory.Server.AbortAsync();
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
            factory.Client.Closed += handler;
            factory.Server.Closed += handler;

            await (closeClientSide ? factory.Client : factory.Server).ShutdownAsync();

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

            using IAcceptor acceptor = factory.Endpoint.CreateAcceptor(new IncomingConnectionOptions
            {
                TransportOptions = new TcpOptions()
                {
                    ListenerBackLog = 1
                }
            }, factory.Logger);

            // TODO: add test once it's possible to create a connection directly. Right now, the connect timeout
            // is handled by the outgoing connection factory.
        }

        [TestCase("tcp", false)]
        [TestCase("ws", false)]
        [TestCase("tcp", true)]
        [TestCase("ws", true)]
        [TestCase("udp", false)]
        public async Task Connection_InformationAsync(string transport, bool secure)
        {
            await using var factory = new ConnectionFactory(transport, secure: secure);

            Assert.That(factory.Client.Socket, Is.AssignableTo<IIPSocket>());
            Assert.That(factory.Server.Socket, Is.AssignableTo<IIPSocket>());

            var clientSocket = (IIPSocket)factory.Client.Socket;
            var serverSocket = (IIPSocket)factory.Server.Socket;

            Assert.That(clientSocket.IsSecure, Is.EqualTo(secure));
            Assert.That(serverSocket.IsSecure, Is.EqualTo(secure));

            Assert.That(clientSocket.RemoteEndPoint, Is.Not.Null);
            Assert.That(clientSocket.LocalEndPoint, Is.Not.Null);

            Assert.That(serverSocket.LocalEndPoint, Is.Not.Null);

            Assert.AreEqual("127.0.0.1", factory.Client.LocalEndpoint!.Host);
            Assert.AreEqual("127.0.0.1", factory.Client.RemoteEndpoint!.Host);
            Assert.That(factory.Client.RemoteEndpoint!.Port, Is.EqualTo(factory.Server.LocalEndpoint!.Port));
            if (transport == "udp")
            {
                Assert.That(serverSocket.RemoteEndPoint, Is.Null);
                Assert.Throws<InvalidOperationException>(() => _ = factory.Server.RemoteEndpoint);
            }
            else
            {
                Assert.That(serverSocket.RemoteEndPoint, Is.Not.Null);
                Assert.That(factory.Client.LocalEndpoint.Port, Is.EqualTo(factory.Server.RemoteEndpoint!.Port));
                Assert.AreEqual("127.0.0.1", factory.Client.RemoteEndpoint.Host);
            }
            Assert.AreEqual(null, factory.Client.RemoteEndpoint["compress"]);
            Assert.That(factory.Client.IsIncoming, Is.False);
            Assert.That(factory.Server.IsIncoming, Is.True);

            Assert.AreEqual(null, factory.Client.Server);
            Assert.AreEqual(factory.Client.RemoteEndpoint.Port, clientSocket.RemoteEndPoint!.Port);
            Assert.AreEqual(factory.Client.LocalEndpoint.Port, clientSocket.LocalEndPoint!.Port);

            Assert.AreEqual("127.0.0.1", clientSocket.LocalEndPoint.Address.ToString());
            Assert.AreEqual("127.0.0.1", clientSocket.RemoteEndPoint.Address.ToString());

            Assert.That($"{factory.Client}", Does.StartWith(clientSocket.GetType().FullName));
            Assert.That($"{factory.Server}", Does.StartWith(serverSocket.GetType().FullName));

            if (transport == "udp")
            {
                Assert.That(clientSocket, Is.AssignableTo<IUdpSocket>());
            }
            else if (transport == "tcp")
            {
                Assert.That(clientSocket, Is.AssignableTo<ITcpSocket>());
            }
            if (transport == "ws")
            {
                Assert.That(clientSocket, Is.AssignableTo<IWSSocket>());
                var wsSocket = (IWSSocket)clientSocket;

                Assert.AreEqual("websocket", wsSocket.Headers["Upgrade"]);
                Assert.AreEqual("Upgrade", wsSocket.Headers["Connection"]);
                Assert.AreEqual("ice.zeroc.com", wsSocket.Headers["Sec-WebSocket-Protocol"]);
                Assert.That(wsSocket.Headers["Sec-WebSocket-Accept"], Is.Not.Null);
            }

            if (secure)
            {
                CollectionAssert.Contains(new List<string> { "tcp", "ws" }, transport);
                var tcpClientSocket = (ITcpSocket)clientSocket;
                var tcpServerSocket = (ITcpSocket)serverSocket;

                Assert.That(tcpClientSocket.CheckCertRevocationStatus, Is.False);
                Assert.That(tcpClientSocket.IsEncrypted, Is.True);
                Assert.That(tcpClientSocket.IsMutuallyAuthenticated, Is.False);
                Assert.That(tcpClientSocket.IsSigned, Is.True);
                Assert.That(tcpClientSocket.LocalCertificate, Is.Null);

                Assert.That(tcpServerSocket.NegotiatedApplicationProtocol, Is.Not.Null);
                if (OperatingSystem.IsMacOS())
                {
                    // APLN doesn't work on macOS (we keep this check to figure out when it will be supported)
                    Assert.That(tcpClientSocket.NegotiatedApplicationProtocol!.ToString(), Is.Empty);
                    Assert.That(tcpServerSocket.NegotiatedApplicationProtocol!.ToString(), Is.Empty);
                }
                else
                {
                    Assert.That(tcpClientSocket.NegotiatedApplicationProtocol!.ToString(),
                                Is.EqualTo(Protocol.Ice2.GetName()));
                    Assert.That(tcpServerSocket.NegotiatedApplicationProtocol!.ToString(),
                                Is.EqualTo(Protocol.Ice2.GetName()));
                }

                Assert.That(tcpClientSocket.RemoteCertificate, Is.Not.Null);
                Assert.That(tcpClientSocket.SslProtocol, Is.Not.Null);

                Assert.That(tcpServerSocket.NegotiatedApplicationProtocol,
                            Is.EqualTo(tcpClientSocket.NegotiatedApplicationProtocol));
                Assert.That(tcpServerSocket.LocalCertificate, Is.Not.Null);
                Assert.That(tcpServerSocket.RemoteCertificate, Is.Null);
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
            }
            else
            {
                Assert.That(factory.Server.IdleTimeout, Is.EqualTo(TimeSpan.FromSeconds(2)));
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
            Assert.That(factory.Client.Options!.KeepAlive, Is.True);
            Assert.That(factory.Server.Options!.KeepAlive, Is.True);
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
            EventHandler handler = (sender, args) =>
            {
                Assert.That(sender, Is.EqualTo(heartbeatOnClient ? factory.Server : factory.Client));
                semaphore.Release();
            };
            if (heartbeatOnClient)
            {
                factory.Client.PingReceived += (sender, args) => Assert.Fail();
                factory.Server.PingReceived += handler;
            }
            else
            {
                factory.Client.PingReceived += handler;
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
                serverConnectionOptions: new() { IdleTimeout = TimeSpan.FromMilliseconds(1000) },
                dispatcher: new InlineDispatcher(async (request, cancel) =>
                {
                    await dispatchSemaphore.WaitAsync(cancel);
                    return new OutgoingResponse(request, Payload.FromVoidReturnValue(request));
                }));

            // Perform an invocation
            Task pingTask = factory.Proxy.IcePingAsync();

            // Make sure we receive few pings while the invocation is pending.
            var semaphore = new SemaphoreSlim(0);
            factory.Client.PingReceived += (sender, args) => semaphore.Release();
            await semaphore.WaitAsync();
            await semaphore.WaitAsync();

            dispatchSemaphore.Release();

            await pingTask;
        }

        [TestCase(Protocol.Ice2, "tcp", false)]
        [TestCase(Protocol.Ice2, "tcp", true)]
        [TestCase(Protocol.Ice2, "ws", false)]
        [TestCase(Protocol.Ice2, "ws", true)]
        [TestCase(Protocol.Ice1, "tcp", false)]
        [TestCase(Protocol.Ice1, "tcp", true)]
        [TestCase(Protocol.Ice2, "coloc", false)]
        [TestCase(Protocol.Ice2, "coloc", true)]
        [Repeat(10)]
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

            // Perform an invocation
            Task pingTask = factory.Proxy.IcePingAsync();
            await waitForDispatchSemaphore.WaitAsync();

            // Shutdown the connection
            Task shutdownTask = (closeClientSide ? factory.Client : factory.Server).ShutdownAsync("message");
            Assert.That(dispatchSemaphore.Release(), Is.EqualTo(0));
            await shutdownTask;

            if (protocol == Protocol.Ice1 && closeClientSide)
            {
                // With Ice1, when closing the connection with a pending invocation, invocations are aborted
                // immediately. The Ice1 protocol doesn't support reliably waiting for the response.
                Assert.ThrowsAsync<ConnectionClosedException>(async () => await factory.Proxy.IcePingAsync());

            }
            else
            {
                // Ensure the invocation is successful
                Assert.DoesNotThrowAsync(async () => await pingTask);
            }

            // Next invocation on the connection should throw ConnectionClosedException
            Assert.ThrowsAsync<ConnectionClosedException>(async () => await factory.Proxy.IcePingAsync());
        }

        [TestCase(false, Protocol.Ice1)]
        [TestCase(true, Protocol.Ice1)]
        [TestCase(false, Protocol.Ice2)]
        [TestCase(true, Protocol.Ice2)]
        [Log(LogAttributeLevel.Debug)]
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
            Task pingTask = factory.Proxy.IcePingAsync();
            await waitForDispatchSemaphore.WaitAsync();

            using var cancelSource = new CancellationTokenSource();
            if (closeClientSide)
            {
                Task shutdownTask = factory.Client.ShutdownAsync("client message", cancelSource.Token);
                Assert.That(shutdownTask.IsCompleted, Is.False);
                cancelSource.Cancel();

                if (protocol != Protocol.Ice1)
                {
                    // Ensure the dispatch is canceled. With Ice1, the dispatch isn't canceled because
                    // stream Reset is not supported.
                    dispatchSemaphore.Wait();
                }

                // The invocation on the connection has been canceled by the shutdown cancellation
                var ex = Assert.ThrowsAsync<OperationCanceledException>(async () => await pingTask);

                if (protocol == Protocol.Ice1)
                {
                    // Client-side Ice1 invocations are canceled immediately on shutdown
                    Assert.That(ex!.Message, Is.EqualTo("connection shutdown"));

                    // Ensure the dispatch is canceled with Ice1 once the connection is forcefully closed on
                    // the client side.
                    dispatchSemaphore.Wait();
                }
                else
                {
                    Assert.That(ex!.Message, Is.EqualTo("dispatch canceled by peer"));
                }
            }
            else
            {
                Task shutdownTask = factory.Server.ShutdownAsync("server message", cancelSource.Token);
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
        [TestCase(Protocol.Ice2, "ws", false)]
        [TestCase(Protocol.Ice2, "ws", true)]
        [TestCase(Protocol.Ice1, "tcp", false)]
        [TestCase(Protocol.Ice1, "tcp", true)]
        [TestCase(Protocol.Ice2, "coloc", false)]
        [TestCase(Protocol.Ice2, "coloc", true)]
        [Log(LogAttributeLevel.Debug)]
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
            Task pingTask = factory.Proxy.IcePingAsync();
            await waitForDispatchSemaphore.WaitAsync();

            if (closeClientSide)
            {
                // Shutdown should trigger the abort of the connection after the close timeout
                await factory.Client.ShutdownAsync();
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
                await factory.Server.ShutdownAsync();
                Assert.ThrowsAsync<ConnectionLostException>(async () => await pingTask);
            }

            semaphore.Release();
        }
    }
}
