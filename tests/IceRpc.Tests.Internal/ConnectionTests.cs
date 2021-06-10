// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using IceRpc.Transports.Internal;
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
        /// <summary>The connection factory is a small helper to allow creating a client and incoming connection
        /// directly from the transport API rather than going through the Communicator/Server APIs.</summary>
        private class ConnectionFactory : IAsyncDisposable
        {
            public Connection Client
            {
                get
                {
                    if (_cachedOutgoingConnection == null)
                    {
                        (_cachedIncomingConnection, _cachedOutgoingConnection) = AcceptAndConnectAsync().Result;
                    }
                    return _cachedOutgoingConnection!;
                }
            }

            public OutgoingConnectionOptions OutgoingConnectionOptions { get; }

            public Endpoint Endpoint { get; }

            public Connection Server
            {
                get
                {
                    if (_cachedIncomingConnection == null)
                    {
                        (_cachedIncomingConnection, _cachedOutgoingConnection) = AcceptAndConnectAsync().Result;
                    }
                    return _cachedIncomingConnection!;
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

            private Connection? _cachedOutgoingConnection;
            private Connection? _cachedIncomingConnection;
            private readonly Server _server;

            public async Task<(Connection, Connection)> AcceptAndConnectAsync()
            {
                Connection outgoingConnection;
                Connection incomingConnection;

                if (Endpoint.IsDatagram)
                {
                    incomingConnection = new Connection(
                        Endpoint.CreateIncomingConnection(_server.ConnectionOptions, _server.Logger),
                        _server);
                    _ = incomingConnection.ConnectAsync(default);
                    outgoingConnection = await ConnectAsync(incomingConnection.LocalEndpoint!);
                }
                else
                {
                    using IAcceptor acceptor = Endpoint.CreateAcceptor(_server.ConnectionOptions, _server.Logger);
                    Task<Connection> serverTask = AcceptAsync(acceptor);
                    Task<Connection> clientTask = ConnectAsync(acceptor.Endpoint);
                    incomingConnection = await serverTask;
                    outgoingConnection = await clientTask;
                }

                return (incomingConnection, outgoingConnection);

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
                        Options = OutgoingConnectionOptions
                    };
                    await connection.ConnectAsync(default);
                    return connection;
                }
            }

            public async ValueTask DisposeAsync()
            {
                if (_cachedOutgoingConnection != null)
                {
                    await _cachedOutgoingConnection.DisposeAsync();
                    await _cachedIncomingConnection!.DisposeAsync();
                }
                await _server.DisposeAsync();
            }

            public ConnectionFactory(
                string transport = "coloc",
                Protocol protocol = Protocol.Ice2,
                bool secure = false,
                OutgoingConnectionOptions? outgoingConnectionOptions = null,
                IncomingConnectionOptions? incomingConnectionOptions = null,
                IDispatcher? dispatcher = null)
            {
                if (secure)
                {
                    outgoingConnectionOptions ??= new();
                    outgoingConnectionOptions.AuthenticationOptions = new()
                    {
                        RemoteCertificateValidationCallback =
                            CertificateValidaton.GetServerCertificateValidationCallback(
                                certificateAuthorities: new X509Certificate2Collection()
                                {
                                    new X509Certificate2("../../../certs/cacert.pem")
                                })
                    };

                    incomingConnectionOptions ??= new();
                    incomingConnectionOptions.AuthenticationOptions = new()
                    {
                        ClientCertificateRequired = false,
                        ServerCertificate = new X509Certificate2("../../../certs/server.p12", "password")
                    };
                }

                if (dispatcher != null)
                {
                    dispatcher = Middleware.Logger(Runtime.DefaultLoggerFactory)(dispatcher);
                }

                _server = new Server { ConnectionOptions = incomingConnectionOptions ?? new(), Dispatcher = dispatcher };
                OutgoingConnectionOptions = outgoingConnectionOptions ?? new();

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
                outgoingConnectionOptions: new()
                {
                    IdleTimeout = idleOnClient ? TimeSpan.FromMilliseconds(500) : TimeSpan.FromHours(1)
                },
                incomingConnectionOptions: new()
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

            Assert.That(factory.Client.ConnectionInformation, Is.AssignableTo<IPConnectionInformation>());
            Assert.That(factory.Server.ConnectionInformation, Is.AssignableTo<IPConnectionInformation>());

            var outgoingConnectionInformation = (IPConnectionInformation)factory.Client.ConnectionInformation;
            var incomingConnectionInformation = (IPConnectionInformation)factory.Server.ConnectionInformation;

            Assert.That(outgoingConnectionInformation.IsSecure, Is.EqualTo(secure));
            Assert.That(incomingConnectionInformation.IsSecure, Is.EqualTo(secure));

            Assert.That(outgoingConnectionInformation.RemoteEndPoint, Is.Not.Null);
            Assert.That(outgoingConnectionInformation.LocalEndPoint, Is.Not.Null);

            Assert.That(incomingConnectionInformation.LocalEndPoint, Is.Not.Null);

            Assert.AreEqual("127.0.0.1", factory.Client.LocalEndpoint!.Host);
            Assert.AreEqual("127.0.0.1", factory.Client.RemoteEndpoint!.Host);
            Assert.That(factory.Client.RemoteEndpoint!.Port, Is.EqualTo(factory.Server.LocalEndpoint!.Port));
            if (transport == "udp")
            {
                Assert.That(incomingConnectionInformation.RemoteEndPoint, Is.Null);
                Assert.Throws<InvalidOperationException>(() => _ = factory.Server.RemoteEndpoint);
            }
            else
            {
                Assert.That(incomingConnectionInformation.RemoteEndPoint, Is.Not.Null);
                Assert.That(factory.Client.LocalEndpoint.Port, Is.EqualTo(factory.Server.RemoteEndpoint!.Port));
                Assert.AreEqual("127.0.0.1", factory.Client.RemoteEndpoint.Host);
            }
            Assert.AreEqual(null, factory.Client.RemoteEndpoint["compress"]);
            Assert.That(factory.Client.IsIncoming, Is.False);
            Assert.That(factory.Server.IsIncoming, Is.True);

            Assert.AreEqual(null, factory.Client.Server);
            Assert.AreEqual(factory.Client.RemoteEndpoint.Port, outgoingConnectionInformation.RemoteEndPoint!.Port);
            Assert.AreEqual(factory.Client.LocalEndpoint.Port, outgoingConnectionInformation.LocalEndPoint!.Port);

            Assert.AreEqual("127.0.0.1", outgoingConnectionInformation.LocalEndPoint.Address.ToString());
            Assert.AreEqual("127.0.0.1", outgoingConnectionInformation.RemoteEndPoint.Address.ToString());

            Assert.That($"{factory.Client}", Does.StartWith(outgoingConnectionInformation.GetType().FullName));
            Assert.That($"{factory.Server}", Does.StartWith(incomingConnectionInformation.GetType().FullName));

            if (transport == "udp")
            {
                Assert.That(outgoingConnectionInformation, Is.AssignableTo<UdpConnectionInformation>());
            }
            else if (transport == "tcp")
            {
                Assert.That(outgoingConnectionInformation, Is.AssignableTo<TcpConnectionInformation>());
            }
            if (transport == "ws")
            {
                Assert.That(outgoingConnectionInformation, Is.AssignableTo<WSConnectionInformation>());
                var wsConnectionInformation = (WSConnectionInformation)outgoingConnectionInformation;

                Assert.AreEqual("websocket", wsConnectionInformation.Headers["Upgrade"]);
                Assert.AreEqual("Upgrade", wsConnectionInformation.Headers["Connection"]);
                Assert.AreEqual("ice.zeroc.com", wsConnectionInformation.Headers["Sec-WebSocket-Protocol"]);
                Assert.That(wsConnectionInformation.Headers["Sec-WebSocket-Accept"], Is.Not.Null);
            }

            if (secure)
            {
                CollectionAssert.Contains(new List<string> { "tcp", "ws" }, transport);
                var tcpOutgoingConnectionInformation = (TcpConnectionInformation)outgoingConnectionInformation;
                var tcpIncomingConnectionInformation = (TcpConnectionInformation)incomingConnectionInformation;

                Assert.That(tcpOutgoingConnectionInformation.CheckCertRevocationStatus, Is.False);
                Assert.That(tcpOutgoingConnectionInformation.IsEncrypted, Is.True);
                Assert.That(tcpOutgoingConnectionInformation.IsMutuallyAuthenticated, Is.False);
                Assert.That(tcpOutgoingConnectionInformation.IsSigned, Is.True);
                Assert.That(tcpOutgoingConnectionInformation.LocalCertificate, Is.Null);

                Assert.That(tcpIncomingConnectionInformation.NegotiatedApplicationProtocol, Is.Not.Null);
                if (OperatingSystem.IsMacOS())
                {
                    // APLN doesn't work on macOS (we keep this check to figure out when it will be supported)
                    Assert.That(tcpOutgoingConnectionInformation.NegotiatedApplicationProtocol!.ToString(), Is.Empty);
                    Assert.That(tcpIncomingConnectionInformation.NegotiatedApplicationProtocol!.ToString(), Is.Empty);
                }
                else
                {
                    Assert.That(tcpOutgoingConnectionInformation.NegotiatedApplicationProtocol!.ToString(),
                                Is.EqualTo(Protocol.Ice2.GetName()));
                    Assert.That(tcpIncomingConnectionInformation.NegotiatedApplicationProtocol!.ToString(),
                                Is.EqualTo(Protocol.Ice2.GetName()));
                }

                Assert.That(tcpOutgoingConnectionInformation.RemoteCertificate, Is.Not.Null);
                Assert.That(tcpOutgoingConnectionInformation.SslProtocol, Is.Not.Null);

                Assert.That(tcpIncomingConnectionInformation.NegotiatedApplicationProtocol,
                            Is.EqualTo(tcpOutgoingConnectionInformation.NegotiatedApplicationProtocol));
                Assert.That(tcpIncomingConnectionInformation.LocalCertificate, Is.Not.Null);
                Assert.That(tcpIncomingConnectionInformation.RemoteCertificate, Is.Null);
            }
        }

        [TestCase(Protocol.Ice1)]
        [TestCase(Protocol.Ice2)]
        public async Task Connection_IdleTimeoutAsync(Protocol protocol)
        {
            await using var factory = new ConnectionFactory(
                "tcp",
                protocol: protocol,
                outgoingConnectionOptions: new()
                {
                    IdleTimeout = TimeSpan.FromSeconds(2)
                },
                incomingConnectionOptions: new()
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
                outgoingConnectionOptions: new()
                {
                    KeepAlive = true
                },
                incomingConnectionOptions: new()
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
                outgoingConnectionOptions: new()
                {
                    IdleTimeout = TimeSpan.FromSeconds(1),
                    KeepAlive = heartbeatOnClient
                },
                incomingConnectionOptions: new()
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
                incomingConnectionOptions: new() { IdleTimeout = TimeSpan.FromMilliseconds(1000) },
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
                outgoingConnectionOptions: new()
                {
                    CloseTimeout = closeClientSide ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(60)
                },
                incomingConnectionOptions: new()
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
