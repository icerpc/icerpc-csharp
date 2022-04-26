// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Internal;
using IceRpc.Slice;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using System.Net;
using System.Net.Security;

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
                    await request.Payload.CompleteAsync();
                    await semaphore.WaitAsync(cancel);
                    return new OutgoingResponse(request);
                }),
                protocol: protocol));

            // Perform an invocation
            Task pingTask = factory.ServicePrx.IcePingAsync();

            if (closeClientSide)
            {
                await factory.ClientConnection.CloseAsync();
                Assert.ThrowsAsync<ObjectDisposedException>(async () => await pingTask);
            }
            else
            {
                await factory.ServerConnection.CloseAsync();
                Assert.ThrowsAsync<ConnectionLostException>(async () => await pingTask);
            }

            semaphore.Release();
        }

        [TestCase("tcp", false)]
        [TestCase("tcp", true)]
        public async Task Connection_InformationAsync(string transport, bool secure)
        {
            var serviceCollection = new IntegrationTestServiceCollection();

            serviceCollection.UseEndpoint(transport, host: "127.0.0.1", port: 0);
            await using var factory = new ConnectionFactory(serviceCollection);

            Assert.That(factory.ClientConnection.NetworkConnectionInformation, Is.Not.Null);
            NetworkConnectionInformation clientInformation = factory.ClientConnection.NetworkConnectionInformation.Value;

            Assert.That(factory.ServerConnection.NetworkConnectionInformation, Is.Not.Null);
            NetworkConnectionInformation serverInformation = factory.ServerConnection.NetworkConnectionInformation.Value;

            var clientLocalIPEndpoint = (IPEndPoint)clientInformation.LocalEndPoint;
            var clientRemoteIPEndpoint = (IPEndPoint)clientInformation.RemoteEndPoint;

            var serverLocalIPEndpoint = (IPEndPoint)serverInformation.LocalEndPoint;
            var serverRemoteIPEndpoint = (IPEndPoint)serverInformation.RemoteEndPoint;

            Assert.That(clientLocalIPEndpoint.Address, Is.EqualTo(IPAddress.Loopback));
            Assert.That(clientRemoteIPEndpoint.Address, Is.EqualTo(IPAddress.Loopback));

            Assert.That(serverLocalIPEndpoint.Address, Is.EqualTo(IPAddress.Loopback));
            Assert.That(serverRemoteIPEndpoint.Address, Is.EqualTo(IPAddress.Loopback));

            Assert.That(clientRemoteIPEndpoint.Port, Is.EqualTo(serverLocalIPEndpoint.Port));
            Assert.That(clientLocalIPEndpoint.Port, Is.EqualTo(serverRemoteIPEndpoint.Port));
        }

        [TestCase("ice")]
        [TestCase("icerpc")]
        public async Task Connection_IdleTimeoutAsync(string protocol)
        {
            await using var factory = new ConnectionFactory(
                new ConnectionTestServiceCollection("tcp", protocol)
                .AddScoped(_ => new TcpClientTransportOptions()
                {
                    IdleTimeout = TimeSpan.FromSeconds(2)
                })
                .AddScoped(_ => new TcpServerTransportOptions()
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
                .AddScoped(_ => new TcpClientTransportOptions()
                {
                    IdleTimeout = TimeSpan.FromMilliseconds(500),
                })
                .AddScoped(_ => new TcpServerTransportOptions()
                {
                    IdleTimeout = TimeSpan.FromMilliseconds(500),
                }),
                clientConnectionOptions: new()
                {
                    KeepAlive = heartbeatOnClient
                },
                serverOptions: new()
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
                        await request.Payload.CompleteAsync();
                        await dispatchSemaphore.WaitAsync(cancel);
                        return new OutgoingResponse(request);
                    }))
                    .AddScoped(_ => new TcpServerTransportOptions() { IdleTimeout = TimeSpan.FromSeconds(1) })
                );

            // Perform an invocation and wait 2 seconds. The connection shouldn't close.
            Task pingTask = factory.ServicePrx.IcePingAsync();
            await Task.Delay(TimeSpan.FromSeconds(2));
            Assert.That(factory.ClientConnection.State, Is.EqualTo(ConnectionState.Active));
            Assert.That(factory.ServerConnection.State, Is.EqualTo(ConnectionState.Active));
            dispatchSemaphore.Release();
            await pingTask;
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
                        await request.Payload.CompleteAsync();
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
                        await request.Payload.CompleteAsync();
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
                        await request.Payload.CompleteAsync();
                        waitForDispatchSemaphore.Release();
                        await semaphore.WaitAsync(cancel);
                        return new OutgoingResponse(request);
                    })
                ),
                clientConnectionOptions: new()
                {
                    CloseTimeout = closeClientSide ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(60)
                },
                serverOptions: new()
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
                    Assert.ThrowsAsync<ObjectDisposedException>(async () => await pingTask);
                }
            }
            else
            {
                // Shutdown should trigger the abort of the connection on the client side after the close timeout
                await factory.ServerConnection.ShutdownAsync();
                Assert.ThrowsAsync<ConnectionLostException>(async () => await pingTask);
            }

            semaphore.Release();
        }

        private class ConnectionTestServiceCollection : IntegrationTestServiceCollection
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
                ServerOptions? serverOptions = null)
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

                    serverOptions ??= new();

                    var serverConnectionOptions = new ConnectionOptions
                    {
                        CloseTimeout = serverOptions.CloseTimeout,
                        ConnectTimeout = serverOptions.ConnectTimeout,
                        Dispatcher = _serviceProvider.GetService<IDispatcher>() ?? serverOptions.Dispatcher,
                        Fields = serverOptions.Fields,
                        IceProtocolOptions = serverOptions.IceProtocolOptions,
                        KeepAlive = serverOptions.KeepAlive
                    };

                    var connection = new Connection(
                        listener.Endpoint,
                        networkConnection,
                        serverConnectionOptions);

                    await connection.ConnectAsync<T>(
                        networkConnection,
                        protocolConnectionFactory,
                        closedEventHandler: null);
                    return connection;
                }

                async Task<Connection> ConnectAsync(Endpoint endpoint)
                {
                    // TODO: refactor test to use connection options correctly.
                    ConnectionOptions connectionOptions = clientConnectionOptions ?? new();
                    connectionOptions = connectionOptions with
                    {
                        AuthenticationOptions = _serviceProvider.GetService<SslClientAuthenticationOptions>(),
                        IsResumable = false,
                        SimpleClientTransport =
                            _serviceProvider.GetRequiredService<IClientTransport<ISimpleNetworkConnection>>(),
                        MultiplexedClientTransport =
                            _serviceProvider.GetRequiredService<IClientTransport<IMultiplexedNetworkConnection>>(),
                        LoggerFactory = _serviceProvider.GetRequiredService<ILoggerFactory>(),
                        RemoteEndpoint = endpoint
                    };

                    var connection = new Connection(connectionOptions);
                    await connection.ConnectAsync(default);
                    return connection;
                }
            }
        }
    }
}
