// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Internal;
using IceRpc.Slice;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace IceRpc.Tests.Internal
{
    [Parallelizable(ParallelScope.All)]
    [Timeout(5000)]
    public class ConnectionTests
    {
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
            await using var factory = new ConnectionFactory(new ConnectionTestServiceCollection(
                transport,
                dispatcher: new InlineDispatcher(async (request, cancel) =>
                {
                    await semaphore.WaitAsync(cancel);
                    return new OutgoingResponse(request);
                }),
                protocol: protocol));

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
            await using var factory = new ConnectionFactory(new ConnectionTestServiceCollection("tcp", protocol));

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
                new ConnectionTestServiceCollection("tcp", protocol)
                .AddScoped(_ => new TcpClientOptions()
                {
                    IdleTimeout = idleOnClient ? TimeSpan.FromMilliseconds(500) : TimeSpan.FromHours(1)
                })
                .AddScoped(_ => new TcpServerOptions()
                {
                    IdleTimeout = idleOnClient ? TimeSpan.FromHours(1) : TimeSpan.FromMilliseconds(500)
                }));

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
            await using ServiceProvider serviceProvider = new InternalTestServiceCollection()
                .UseTransport("tcp")
                .UseProtocol(protocol)
                .BuildServiceProvider();

            IListener listener = protocol == Protocol.Ice1.Code ?
                serviceProvider.GetRequiredService<IListener<ISimpleNetworkConnection>>() :
                serviceProvider.GetRequiredService<IListener<IMultiplexedNetworkConnection>>();

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
        [Log(LogAttributeLevel.Trace)]
        public async Task Connection_InformationAsync(string transport, bool secure)
        {
            var serviceCollection = new InternalTestServiceCollection();
            if (secure)
            {
                serviceCollection.UseTls();
            }
            serviceCollection.UseEndpoint(transport, host: "127.0.0.1", port: 0);
            await using var factory = new ConnectionFactory(serviceCollection);

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
                new ConnectionTestServiceCollection("tcp", protocol)
                .AddScoped(_ => new TcpClientOptions()
                {
                    IdleTimeout = TimeSpan.FromSeconds(2)
                })
                .AddScoped(_ => new TcpServerOptions()
                {
                    IdleTimeout = TimeSpan.FromSeconds(3)
                }));

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
                new ConnectionTestServiceCollection(protocol: protocol),
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
                new ConnectionTestServiceCollection("tcp", protocol)
                .AddScoped(_ => new TcpClientOptions()
                {
                    IdleTimeout = TimeSpan.FromMilliseconds(500),
                })
                .AddScoped(_ => new TcpServerOptions()
                {
                    IdleTimeout = TimeSpan.FromMilliseconds(500),
                }),
                clientConnectionOptions: new()
                {
                    KeepAlive = heartbeatOnClient
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
                new ConnectionTestServiceCollection(
                    "tcp",
                    protocol,
                    dispatcher: new InlineDispatcher(async (request, cancel) =>
                    {
                        await dispatchSemaphore.WaitAsync(cancel);
                        return new OutgoingResponse(request);
                    }))
                    .AddScoped(_ => new TcpServerOptions() { IdleTimeout = TimeSpan.FromSeconds(1) })
                );

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
                new ConnectionTestServiceCollection(
                    transport,
                    protocol,
                    dispatcher: new InlineDispatcher(async (request, cancel) =>
                    {
                        waitForDispatchSemaphore.Release();
                        await dispatchSemaphore.WaitAsync(cancel);
                        return new OutgoingResponse(request);
                    })));

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
                new ConnectionTestServiceCollection(
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
                        return new OutgoingResponse(request);
                    })));

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
                new ConnectionTestServiceCollection(
                    transport,
                    protocol: protocol,
                    dispatcher: new InlineDispatcher(async (request, cancel) =>
                    {
                        waitForDispatchSemaphore.Release();
                        await semaphore.WaitAsync(cancel);
                        return new OutgoingResponse(request);
                    })
                ),
                clientConnectionOptions: new()
                {
                    CloseTimeout = closeClientSide ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(60)
                },
                serverConnectionOptions: new()
                {
                    CloseTimeout = closeClientSide ? TimeSpan.FromSeconds(60) : TimeSpan.FromSeconds(1)
                });

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

        private class ConnectionTestServiceCollection : InternalTestServiceCollection
        {
            internal ConnectionTestServiceCollection(
                string transport = "coloc",
                ProtocolCode? protocol = null,
                IDispatcher? dispatcher = null)
            {
                this.UseTransport(transport);
                if (protocol != null)
                {
                    this.UseProtocol(protocol.Value);
                }
                if (dispatcher != null)
                {
                    this.AddScoped(_ => dispatcher);
                }
            }
        }

        private class ConnectionFactory : IAsyncDisposable
        {
            public Connection ClientConnection { get; }

            public Connection ServerConnection { get; }

            public IServicePrx ServicePrx { get; }

            private readonly ServiceProvider _serviceProvider;

            public async ValueTask DisposeAsync()
            {
                await ClientConnection.DisposeAsync();
                await ServerConnection.DisposeAsync();
                await _serviceProvider.DisposeAsync();
            }

            internal ConnectionFactory(
                IServiceCollection serviceCollection,
                ConnectionOptions? clientConnectionOptions = null,
                ConnectionOptions? serverConnectionOptions = null)
            {
                _serviceProvider = serviceCollection.BuildServiceProvider();

                (ServerConnection, ClientConnection) =
                    _serviceProvider.GetRequiredService<Protocol>() == Protocol.Ice1 ?
                        PerformAcceptAndConnectAsync(Ice1Protocol.Instance.ProtocolConnectionFactory).Result :
                        PerformAcceptAndConnectAsync(Ice2Protocol.Instance.ProtocolConnectionFactory).Result;

                // Don't use empty path because Ice1 doesn't accept it
                ServicePrx = new ServicePrx(Proxy.FromConnection(ClientConnection, path: "/foo"));

                async Task<(Connection, Connection)> PerformAcceptAndConnectAsync<T>(
                    IProtocolConnectionFactory<T> protocolConnectionFactory) where T : INetworkConnection
                {
                    IListener<T> listener = _serviceProvider.GetRequiredService<IListener<T>>();
                    Task<Connection> serverTask = AcceptAsync(listener, protocolConnectionFactory);
                    Task<Connection> clientTask = ConnectAsync(listener.Endpoint);
                    return (await serverTask, await clientTask);
                }

                async Task<Connection> AcceptAsync<T>(
                    IListener<T> listener,
                    IProtocolConnectionFactory<T> protocolConnectionFactory) where T : INetworkConnection
                {
                    T networkConnection = await listener.AcceptAsync();

                    var connection = new Connection(networkConnection, listener.Endpoint.Protocol)
                    {
                        Dispatcher = _serviceProvider.GetService<IDispatcher>(),
                        Options = serverConnectionOptions ?? new(),
                        LoggerFactory = _serviceProvider.GetRequiredService<ILoggerFactory>()
                    };
                    await connection.ConnectAsync<T>(networkConnection,
                                                     protocolConnectionFactory,
                                                     closedEventHandler: null);
                    return connection;
                }

                async Task<Connection> ConnectAsync(Endpoint endpoint)
                {
                    var connection = new Connection
                    {
                        SimpleClientTransport =
                                _serviceProvider.GetRequiredService<IClientTransport<ISimpleNetworkConnection>>(),
                        MultiplexedClientTransport =
                                _serviceProvider.GetRequiredService<IClientTransport<IMultiplexedNetworkConnection>>(),
                        Options = clientConnectionOptions ?? new(),
                        LoggerFactory = _serviceProvider.GetRequiredService<ILoggerFactory>(),
                        RemoteEndpoint = endpoint
                    };
                    await connection.ConnectAsync(default);
                    return connection;
                }
            }
        }
    }
}
