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
        [TestCase("icerpc", "tcp", false)]
        [TestCase("icerpc", "tcp", true)]
        [TestCase("ice", "tcp", false)]
        [TestCase("ice", "tcp", true)]
        [TestCase("icerpc", "coloc", false)]
        [TestCase("icerpc", "coloc", true)]
        [TestCase("ice", "coloc", false)]
        [TestCase("ice", "coloc", true)]
        public async Task Connection_CloseAsync(string protocol, string transport, bool closeClientSide)
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

        [TestCase("ice", false)]
        [TestCase("ice", true)]
        [TestCase("icerpc", false)]
        [TestCase("icerpc", true)]
        public async Task Connection_ClosedEventAsync(string protocol, bool closeClientSide)
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

        [TestCase("ice", false)]
        [TestCase("ice", true)]
        [TestCase("icerpc", false)]
        [TestCase("icerpc", true)]
        public async Task Connection_CloseOnIdleAsync(string protocol, bool idleOnClient)
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

        [TestCase("ice")]
        [TestCase("icerpc")]
        public async Task Connection_ConnectTimeoutAsync(string protocol)
        {
            await using ServiceProvider serviceProvider = new InternalTestServiceCollection()
                .UseTransport("tcp")
                .UseProtocol(protocol)
                .BuildServiceProvider();

            IListener listener = protocol == Protocol.Ice.Name ?
                serviceProvider.GetRequiredService<IListener<ISimpleNetworkConnection>>() :
                serviceProvider.GetRequiredService<IListener<IMultiplexedNetworkConnection>>();

            await using var connection = new Connection(new ConnectionOptions
            {
                ConnectTimeout = TimeSpan.FromMilliseconds(100),
                RemoteEndpoint = listener.Endpoint,
            });

            Assert.ThrowsAsync<ConnectTimeoutException>(() => connection.ConnectAsync(default));
        }

        [TestCase("tcp", false)]
        [TestCase("tcp", true)]
        [TestCase("udp", false)]
        public async Task Connection_InformationAsync(string transport, bool secure)
        {
            var serviceCollection = new InternalTestServiceCollection();
            if (transport == "udp")
            {
                serviceCollection.UseProtocol("ice");
            }

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

            Assert.That(clientInformation?.LocalEndpoint.Host, Is.EqualTo("127.0.0.1"));
            Assert.That(clientInformation?.RemoteEndpoint.Host, Is.EqualTo("127.0.0.1"));
            Assert.That(clientInformation?.RemoteEndpoint!.Port, Is.EqualTo(serverInformation?.LocalEndpoint!.Port));
            if (transport != "udp")
            {
                Assert.That(clientInformation?.LocalEndpoint!.Port, Is.EqualTo(serverInformation?.RemoteEndpoint!.Port));
                Assert.That(clientInformation?.RemoteEndpoint!.Host, Is.EqualTo("127.0.0.1"));
            }
            Assert.That(factory.ClientConnection.IsServer, Is.False);
            Assert.That(factory.ServerConnection.IsServer, Is.True);

            if (secure)
            {
                Assert.That(transport, Is.EqualTo("tcp"));
                Assert.That(clientInformation?.RemoteCertificate, Is.Not.Null);
                Assert.That(serverInformation?.RemoteCertificate, Is.Null);
            }
        }

        [TestCase("ice")]
        [TestCase("icerpc")]
        public async Task Connection_IdleTimeoutAsync(string protocol)
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

            if (protocol == "ice")
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

        [TestCase("ice", false)]
        [TestCase("ice", true)]
        [TestCase("icerpc", false)]
        [TestCase("icerpc", true)]
        public async Task Connection_KeepAliveOnIdleAsync(string protocol, bool heartbeatOnClient)
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

        [TestCase("ice")]
        [TestCase("icerpc")]
        public async Task Connection_KeepAliveOnInvocationAsync(string protocol)
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

        [TestCase("icerpc", false)]
        [TestCase("ice", false)]
        [TestCase("icerpc", true)]
        [TestCase("ice", true)]
        public async Task Connection_Resumable(string protocol, bool closeClientSide)
        {
            // Don't use the connection factory since it doesn't create resumable connections.
            Connection? serverConnection = null;
            await using ServiceProvider serviceProvider = new IntegrationTestServiceCollection()
                .UseResumableConnection()
                .UseProtocol(protocol)
                .AddTransient<IDispatcher>(_ => new InlineDispatcher((request, cancel) =>
                    {
                        serverConnection = request.Connection;
                        return new(new OutgoingResponse(request));
                    }))
                .BuildServiceProvider();

            ServicePrx prx = serviceProvider.GetProxy<ServicePrx>();

            await prx.IcePingAsync(default);

            Connection clientConnection = prx.Proxy.Connection!;
            Assert.That(serverConnection, Is.Not.Null);

            if (closeClientSide)
            {
                await clientConnection.ShutdownAsync(default);
                Assert.That(clientConnection.State, Is.EqualTo(ConnectionState.NotConnected));
                Assert.That(serverConnection!.State, Is.GreaterThan(ConnectionState.Active));
            }
            else
            {
                await serverConnection!.ShutdownAsync(default);
                Assert.That(serverConnection.State, Is.EqualTo(ConnectionState.Closed));
            }

            Assert.That(
                clientConnection.State,
                Is.GreaterThan(ConnectionState.Active).Or.EqualTo(ConnectionState.NotConnected));

            Assert.DoesNotThrowAsync(() => prx.IcePingAsync());

            Assert.That(prx.Proxy.Connection, Is.EqualTo(clientConnection));
            Assert.That(clientConnection.State, Is.EqualTo(ConnectionState.Active));
        }

        [TestCase("icerpc", false)]
        [TestCase("ice", false)]
        [TestCase("icerpc", true)]
        [TestCase("ice", true)]
        public async Task Connection_NotResumable(string protocol, bool closeClientSide)
        {
            await using var factory = new ConnectionFactory(
                new ConnectionTestServiceCollection(
                    protocol: protocol,
                    dispatcher: new InlineDispatcher((request, cancel) => new(new OutgoingResponse(request)))));

            await factory.ServicePrx.IcePingAsync(default);
            if (closeClientSide)
            {
                await factory.ClientConnection.ShutdownAsync(default);
                Assert.That(factory.ClientConnection.State, Is.EqualTo(ConnectionState.Closed));
            }
            else
            {
                await factory.ServerConnection.ShutdownAsync(default);
                Assert.That(factory.ServerConnection.State, Is.EqualTo(ConnectionState.Closed));
            }

            Assert.ThrowsAsync<ConnectionClosedException>(() => factory.ServicePrx.IcePingAsync());
        }

        [TestCase("icerpc", "tcp", false)]
        [TestCase("icerpc", "tcp", true)]
        [TestCase("ice", "tcp", false)]
        [TestCase("ice", "tcp", true)]
        [TestCase("icerpc", "coloc", false)]
        [TestCase("icerpc", "coloc", true)]
        public async Task Connection_ShutdownAsync(string protocol, string transport, bool closeClientSide)
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

            if (protocol == "ice" && closeClientSide)
            {
                await shutdownTask;

                // With the Ice protocol, when closing the connection with a pending invocation, invocations are
                // canceled immediately. The Ice protocol doesn't support reliably waiting for the response.
                Assert.ThrowsAsync<OperationCanceledException>(async () => await pingTask);
                Assert.That(dispatchSemaphore.Release(), Is.EqualTo(0));
            }
            else
            {
                Assert.That(dispatchSemaphore.Release(), Is.EqualTo(0));
                await shutdownTask;

                // Ensure the invocation is successful.
                Assert.DoesNotThrowAsync(async () => await pingTask);
            }
        }

        [TestCase(false, "ice")]
        [TestCase(true, "ice")]
        [TestCase(false, "icerpc")]
        [TestCase(true, "icerpc")]
        public async Task Connection_ShutdownCancellationAsync(bool closeClientSide, string protocol)
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
                if (protocol == "ice")
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

        [TestCase("icerpc", "tcp", false)]
        [TestCase("icerpc", "tcp", true)]
        [TestCase("ice", "tcp", false)]
        [TestCase("ice", "tcp", true)]
        [TestCase("icerpc", "coloc", false)]
        [TestCase("icerpc", "coloc", true)]
        public async Task Connection_ShutdownAsync_CloseTimeoutAsync(
            string protocol,
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
                if (protocol == "ice")
                {
                    // Invocations are canceled immediately on shutdown with Ice
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
                string? protocol = null,
                IDispatcher? dispatcher = null)
            {
                this.UseTransport(transport);
                if (protocol != null)
                {
                    this.UseProtocol(protocol);
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
                ConnectionOptions? serverConnectionOptions = null) // TODO: should not use client options for server
            {
                _serviceProvider = serviceCollection.BuildServiceProvider();

                (ServerConnection, ClientConnection) =
                    _serviceProvider.GetRequiredService<Protocol>() == Protocol.Ice ?
                        PerformAcceptAndConnectAsync(IceProtocol.Instance.ProtocolConnectionFactory).Result :
                        PerformAcceptAndConnectAsync(IceRpcProtocol.Instance.ProtocolConnectionFactory).Result;

                // Don't use empty path because Ice doesn't accept it
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

                    serverConnectionOptions ??= new();

                    var connection = new Connection(
                        networkConnection,
                        listener.Endpoint.Protocol,
                        serverConnectionOptions.CloseTimeout);

                    await connection.ConnectAsync<T>(
                        networkConnection,
                        _serviceProvider.GetService<IDispatcher>() ?? serverConnectionOptions.Dispatcher,
                        protocolConnectionFactory,
                        serverConnectionOptions.ConnectTimeout,
                        serverConnectionOptions.IncomingFrameMaxSize,
                        serverConnectionOptions.KeepAlive,
                        closedEventHandler: null);
                    return connection;
                }

                async Task<Connection> ConnectAsync(Endpoint endpoint)
                {
                    // TODO: refactor test to use connection options correctly.
                    ConnectionOptions connectionOptions = clientConnectionOptions?.Clone() ?? new();
                    connectionOptions.IsResumable = false;
                    connectionOptions.SimpleClientTransport =
                         _serviceProvider.GetRequiredService<IClientTransport<ISimpleNetworkConnection>>();

                    connectionOptions.MultiplexedClientTransport =
                        _serviceProvider.GetRequiredService<IClientTransport<IMultiplexedNetworkConnection>>();

                    connectionOptions.LoggerFactory = _serviceProvider.GetRequiredService<ILoggerFactory>();
                    connectionOptions.RemoteEndpoint = endpoint;

                    var connection = new Connection(connectionOptions);
                    await connection.ConnectAsync(default);
                    return connection;
                }
            }
        }
    }
}
