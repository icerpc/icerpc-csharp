// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Slice;
using IceRpc.Transports;
using IceRpc.Transports.Internal;
using IceRpc.Transports.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace IceRpc.Tests;

[Parallelizable(ParallelScope.All)]
[Timeout(5000)]
public class ConnectionTests
{
    /// <summary>Verifies that a connection is closed after being idle.</summary>
    [Test]
    public async Task Close_on_idle(
        [Values(true, false)] bool idleOnClient,
        [Values("ice", "icerpc")] string protocol)
    {
        // Arrange
        using var start = new SemaphoreSlim(0);
        using var hold = new SemaphoreSlim(0);
        var tcpServerTransportOptions = new TcpServerTransportOptions
        {
            IdleTimeout = idleOnClient ? TimeSpan.FromHours(1) : TimeSpan.FromMilliseconds(500),
        };

        var tcpClientTransportOptions = new TcpClientTransportOptions
        {
            IdleTimeout = idleOnClient ? TimeSpan.FromMilliseconds(500) : TimeSpan.FromHours(1),
        };

        Connection? serverConnection = null;
        var dispatcher = new InlineDispatcher(async (request, cancel) =>
        {
            serverConnection = request.Connection;
            start.Release();
            await hold.WaitAsync(cancel);
            return new OutgoingResponse(request);
        });

        var serverConnectionClosed = new TaskCompletionSource();
        var clientConnectionClosed = new TaskCompletionSource();

        await using var provider = new ConnectionServiceCollection(protocol)
            .UseTcp(tcpServerTransportOptions, tcpClientTransportOptions)
            .UseConnectionOptions(new ConnectionOptions
            {
                OnClose = (_, _) => clientConnectionClosed.SetResult()
            })
            .UseServerOptions(new ServerOptions
            {
                OnClose = (_, _) => serverConnectionClosed.SetResult()
            })
            .UseDispatcher(dispatcher)
            .BuildServiceProvider();

        var server = provider.GetRequiredService<Server>();
        var clientConnection = provider.GetRequiredService<Connection>();
        var proxy = Proxy.FromConnection(clientConnection, "/foo");

        var invokeTask = proxy.Invoker.InvokeAsync(new OutgoingRequest(proxy));
        await start.WaitAsync();

        // Act
        hold.Release(); // let the connection idle

        // Assert
        Assert.That(async () => await serverConnectionClosed.Task, Throws.Nothing);
        Assert.That(async () => await clientConnectionClosed.Task, Throws.Nothing);
    }

    /// <summary>Verifies that closing the connection aborts the invocations.</summary>
    [Test]
    public async Task Closing_the_client_connection_aborts_the_invocations([Values("ice", "icerpc")] string protocol)
    {
        // Arrange
        using var start = new SemaphoreSlim(0);
        using var hold = new SemaphoreSlim(0);
        var dispatcher = new InlineDispatcher(async (request, cancel) =>
        {
            start.Release();
            await hold.WaitAsync(cancel);
            return new OutgoingResponse(request);
        });

        await using var provider = new ConnectionServiceCollection(protocol)
            .UseDispatcher(dispatcher)
            .BuildServiceProvider();
        var server = provider.GetRequiredService<Server>();
        var connection = provider.GetRequiredService<Connection>();

        var proxy = Proxy.FromConnection(connection, "/foo");

        var invokeTask = proxy.Invoker.InvokeAsync(new OutgoingRequest(proxy));
        await start.WaitAsync(); // Wait for dispatch to start

        // Act
        await connection.CloseAsync();

        // Assert
        Assert.That(async () => await invokeTask, Throws.TypeOf<ObjectDisposedException>());
    }

    /// <summary>Verifies that closing the server connection aborts the invocations.</summary>
    [Test]
    public async Task Closing_the_server_connection_aborts_the_invocations([Values("ice", "icerpc")] string protocol)
    {
        // Arrange
        using var start = new SemaphoreSlim(0);
        using var hold = new SemaphoreSlim(0);
        Connection? serverConnection = null;
        var dispatcher = new InlineDispatcher(async (request, cancel) =>
        {
            serverConnection = request.Connection;
            start.Release();
            await hold.WaitAsync(cancel);
            return new OutgoingResponse(request);
        });

        await using var provider = new ConnectionServiceCollection(protocol)
            .UseDispatcher(dispatcher)
            .BuildServiceProvider();
        var server = provider.GetRequiredService<Server>();
        var connection = provider.GetRequiredService<Connection>();

        var proxy = Proxy.FromConnection(connection, "/foo");

        var invokeTask = proxy.Invoker.InvokeAsync(new OutgoingRequest(proxy));
        await start.WaitAsync(); // Wait for dispatch to start

        // Act
        await serverConnection!.CloseAsync();

        // Assert
        Assert.That(async () => await invokeTask, Throws.TypeOf<ConnectionLostException>());
    }

    /// <summary>Verifies that closing the connection raises the connection closed event.</summary>
    [Test]
    public async Task Connection_closed_event(
        [Values("ice", "icerpc")] string protocol,
        [Values(true, false)] bool closeClientConnection)
    {
        // Arrange
        Connection? serverConnection = null;
        var dispatcher = new InlineDispatcher((request, cancel) =>
        {
            serverConnection = request.Connection;
            return new(new OutgoingResponse(request));
        });

        var serverConnectionClosed = new TaskCompletionSource<object?>();
        var clientConnectionClosed = new TaskCompletionSource<object?>();

        await using var provider = new ConnectionServiceCollection(protocol)
            .UseDispatcher(dispatcher)
            .UseConnectionOptions(new ConnectionOptions
            {
                OnClose = (_, _) => clientConnectionClosed.SetResult(null)
            })
            .UseServerOptions(new ServerOptions
            {
                OnClose = (_, _) => serverConnectionClosed.SetResult(null)
            })
            .BuildServiceProvider();
        var server = provider.GetRequiredService<Server>();
        var clientConnection = provider.GetRequiredService<Connection>();

        var proxy = Proxy.FromConnection(clientConnection, "/foo");

        await proxy.Invoker.InvokeAsync(new OutgoingRequest(proxy));

        // Act
        await (closeClientConnection ? clientConnection : serverConnection!).CloseAsync();

        // Assert
        Assert.That(async () => await serverConnectionClosed.Task, Throws.Nothing);
        Assert.That(async () => await clientConnectionClosed.Task, Throws.Nothing);
    }

    /// <summary>Verifies that connect establishment timeouts after the <see cref="ConnectionOptions.ConnectTimeout"/>
    /// time period.</summary>
    [Test]
    public async Task Connect_timeout([Values("ice", "icerpc")] string protocol)
    {
        // Arrange
        var tcpServerTransport = new TcpServerTransport();
        var slicServerTransport = new SlicServerTransport(tcpServerTransport);

        var listener = slicServerTransport.Listen($"{protocol}://127.0.0.1:0", null, NullLogger.Instance);
        await using var connection = new Connection(new ConnectionOptions
        {
            RemoteEndpoint = listener.Endpoint,
            ConnectTimeout = TimeSpan.FromMilliseconds(100),
        });

        // Assert
        Assert.That(async () => await connection.ConnectAsync(default), Throws.TypeOf<ConnectTimeoutException>());
    }

    [Test]
    public async Task Non_resumable_connection_cannot_reconnect([Values("ice", "icerpc")] string protocol)
    {
        // Arrange
        await using var provider = new ConnectionServiceCollection(protocol)
            .UseDispatcher(new InlineDispatcher((request, cancel) => new(new OutgoingResponse(request))))
            .BuildServiceProvider();
        var server = provider.GetRequiredService<Server>();
        var connection = provider.GetRequiredService<Connection>();

        var proxy = Proxy.FromConnection(connection, "/foo");

        await proxy.Invoker.InvokeAsync(new OutgoingRequest(proxy));
        await connection.ShutdownAsync(default);

        // Act/Assert
        Assert.That(
            async () => await connection.InvokeAsync(new OutgoingRequest(new Proxy(Protocol.IceRpc)), default),
            Throws.TypeOf<ConnectionClosedException>());
    }

    [Test]
    public async Task Resumable_connection_can_reconnect([Values("ice", "icerpc")] string protocol)
    {
        // Arrange
        await using var provider = new ConnectionServiceCollection(protocol)
            .UseDispatcher(new InlineDispatcher((request, cancel) => new(new OutgoingResponse(request))))
            .UseConnectionOptions(new ConnectionOptions { IsResumable = true })
            .BuildServiceProvider();
        var server = provider.GetRequiredService<Server>();
        var connection = provider.GetRequiredService<Connection>();

        var proxy = Proxy.FromConnection(connection, "/foo");

        await proxy.Invoker.InvokeAsync(new OutgoingRequest(proxy));
        await connection.ShutdownAsync(default);

        // Act
        var response = await proxy.Invoker.InvokeAsync(new OutgoingRequest(proxy));

        // Assert
        Assert.That(response.ResultType, Is.EqualTo(ResultType.Success));
    }

    [Test]
    public async Task Connect_sets_network_connection_information([Values("ice", "icerpc")] string protocol)
    {
        // Arrange
        await using var provider = new ConnectionServiceCollection(protocol)
            .UseDispatcher(new InlineDispatcher((request, cancel) => new(new OutgoingResponse(request))))
            .UseConnectionOptions(new ConnectionOptions { IsResumable = true })
            .BuildServiceProvider();
        var server = provider.GetRequiredService<Server>();
        var connection = provider.GetRequiredService<Connection>();
        var networkConnectionInformation = connection.NetworkConnectionInformation;

        // Act
        await connection.ConnectAsync(default);

        // Assert
        Assert.That(networkConnectionInformation, Is.Null);
        Assert.That(connection.NetworkConnectionInformation, Is.Not.Null);
    }

    [Test]
    public async Task Keep_alive_on_idle(
        [Values("ice", "icerpc")] string protocol,
        [Values(true, false)] bool keepAliveOnClient)
    {
        // Arrange
        var tcpServerTransportOptions = new TcpServerTransportOptions
        {
            IdleTimeout = TimeSpan.FromMilliseconds(500),
        };

        var tcpClientTransportOptions = new TcpClientTransportOptions
        {
            IdleTimeout = TimeSpan.FromMilliseconds(500),
        };

        await using var provider = new ConnectionServiceCollection(protocol)
            .UseTcp(tcpServerTransportOptions, tcpClientTransportOptions)
            .UseConnectionOptions(new ConnectionOptions { KeepAlive = keepAliveOnClient })
            .UseServerOptions(new ServerOptions { KeepAlive = !keepAliveOnClient })
            .BuildServiceProvider();

        var server = provider.GetRequiredService<Server>();
        var clientConnection = provider.GetRequiredService<Connection>();

        // Act
        await clientConnection.ConnectAsync();

        // Assert
        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.That(clientConnection.State, Is.EqualTo(ConnectionState.Active));
    }

    [Test]
    public async Task Keep_alive_on_invocation([Values("ice", "icerpc")] string protocol)
    {
        // Arrange
        using var start = new SemaphoreSlim(0);
        using var hold = new SemaphoreSlim(0);
        var tcpServerTransportOptions = new TcpServerTransportOptions
        {
            IdleTimeout = TimeSpan.FromMilliseconds(500),
        };

        var tcpClientTransportOptions = new TcpClientTransportOptions
        {
            IdleTimeout = TimeSpan.FromMilliseconds(500),
        };

        var dispatcher = new InlineDispatcher(async (request, cancel) =>
        {
            start.Release();
            await hold.WaitAsync(CancellationToken.None);
            return new OutgoingResponse(request);
        });

        await using var provider = new ConnectionServiceCollection(protocol)
            .UseTcp(tcpServerTransportOptions, tcpClientTransportOptions)
            .UseDispatcher(dispatcher)
            .BuildServiceProvider();

        var server = provider.GetRequiredService<Server>();
        var clientConnection = provider.GetRequiredService<Connection>();
        var proxy = ServicePrx.FromConnection(clientConnection, "/path");
        var pingTask = proxy.IcePingAsync();
        await start.WaitAsync();

        // Act
        await Task.Delay(TimeSpan.FromSeconds(2));

        // Assert
        Assert.That(clientConnection.State, Is.EqualTo(ConnectionState.Active));
        hold.Release();
        Assert.That(async () => await pingTask, Throws.Nothing);
    }

    [Test]
    public async Task Shutdown_connection(
        [Values("ice", "icerpc")] string protocol,
        [Values(true, false)] bool closeClientSide)
    {
        // Arrange
        using var start = new SemaphoreSlim(0);
        using var hold = new SemaphoreSlim(0);
        Connection? serverConnection = null;
        var dispatcher = new InlineDispatcher(async (request, cancel) =>
        {
            serverConnection = request.Connection;
            start.Release();
            await hold.WaitAsync(CancellationToken.None);
            return new OutgoingResponse(request);
        });

        await using var provider = new ConnectionServiceCollection(protocol)
            .UseDispatcher(dispatcher)
            .BuildServiceProvider();

        var server = provider.GetRequiredService<Server>();
        var clientConnection = provider.GetRequiredService<Connection>();
        var proxy = ServicePrx.FromConnection(clientConnection, "/path");
        var pingTask = proxy.IcePingAsync();
        await start.WaitAsync();

        // Act
        var shutdownTask = (closeClientSide ? clientConnection : serverConnection!).ShutdownAsync(CancellationToken.None);

        // Assert
        if (closeClientSide && protocol == "ice")
        {
            await shutdownTask;

            // With the Ice protocol, when closing the connection with a pending invocation, invocations are
            // canceled immediately. The Ice protocol doesn't support reliably waiting for the response.
            Assert.ThrowsAsync<OperationCanceledException>(async () => await pingTask);
            Assert.That(hold.Release(), Is.EqualTo(0));
        }
        else
        {
            Assert.That(hold.Release(), Is.EqualTo(0));
            await shutdownTask;

            // Ensure the invocation is successful.
            Assert.DoesNotThrowAsync(async () => await pingTask);
        }
    }

    [Test]
    public async Task Shutdown_cancellation(
        [Values("ice", "icerpc")] string protocol,
        [Values(true, false)] bool closeClientSide)
    {
        // Arrange
        using var start = new SemaphoreSlim(0);
        using var hold = new SemaphoreSlim(0);

        Connection? serverConnection = null;
        using var shutdownCancelationSource = new CancellationTokenSource();
        var dispatchCompletionSource = new TaskCompletionSource();
        var dispatcher = new InlineDispatcher(async (request, cancel) =>
        {
            try
            {
                serverConnection = request.Connection;
                start.Release();
                await hold.WaitAsync(cancel);
                return new OutgoingResponse(request);
            }
            catch (OperationCanceledException)
            {
                dispatchCompletionSource.SetResult();
                throw;
            }
        });

        await using var provider = new ConnectionServiceCollection(protocol)
            .UseDispatcher(dispatcher)
            .BuildServiceProvider();

        var server = provider.GetRequiredService<Server>();
        var clientConnection = provider.GetRequiredService<Connection>();
        var proxy = ServicePrx.FromConnection(clientConnection, "/path");
        var pingTask = proxy.IcePingAsync();
        await start.WaitAsync();
        var shutdownTask = (closeClientSide ? clientConnection : serverConnection!).ShutdownAsync(shutdownCancelationSource.Token);

        // Act
        shutdownCancelationSource.Cancel();

        // Assert
        if (closeClientSide)
        {
            await shutdownTask;

            Assert.ThrowsAsync<OperationCanceledException>(async () => await pingTask);
            Assert.That(async () => await dispatchCompletionSource.Task, Throws.Nothing);
        }
        else
        {
            Assert.That(shutdownTask.IsCompleted, Is.False);
            Assert.That(async () => await dispatchCompletionSource.Task, Throws.Nothing);

            if (protocol == "ice")
            {
                Assert.That(async () => await pingTask, Throws.TypeOf<DispatchException>());
            }
            else
            {
                Assert.That(async () => await pingTask, Throws.TypeOf<OperationCanceledException>());
            }
        }
    }

    [Test]
    public async Task Close_timeout(
        [Values("ice", "icerpc")] string protocol,
        [Values(true, false)] bool closeClientSide)
    {
        // Arrange
        using var start = new SemaphoreSlim(0);
        using var hold = new SemaphoreSlim(0);

        Connection? serverConnection = null;
        var dispatcher = new InlineDispatcher(async (request, cancel) =>
        {
            serverConnection = request.Connection;
            start.Release();
            await hold.WaitAsync(cancel);
            return new OutgoingResponse(request);
        });

        await using var provider = new ConnectionServiceCollection(protocol)
            .UseDispatcher(dispatcher)
            .UseConnectionOptions(
                new ConnectionOptions
                {
                    CloseTimeout = closeClientSide ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(60)
                })
            .UseServerOptions(
                new ServerOptions
                {
                    CloseTimeout = closeClientSide ? TimeSpan.FromSeconds(60) : TimeSpan.FromSeconds(1)
                })
            .BuildServiceProvider();

        var server = provider.GetRequiredService<Server>();
        var clientConnection = provider.GetRequiredService<Connection>();
        var proxy = ServicePrx.FromConnection(clientConnection, "/path");
        var pingTask = proxy.IcePingAsync();
        await start.WaitAsync();

        // Act
        _ = (closeClientSide ? clientConnection : serverConnection!).ShutdownAsync(default);

        // Assert
        if (closeClientSide)
        {
            // Shutdown should trigger the abort of the connection after the close timeout
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
            Assert.ThrowsAsync<ConnectionLostException>(async () => await pingTask);
        }
        hold.Release();
    }
}

public class ConnectionServiceCollection : ServiceCollection
{
    public ConnectionServiceCollection(string protocol = "icerpc")
    {
        this.UseColoc();
        this.UseSlic();
        this.UseProtocol(protocol == "ice" ? Protocol.Ice : Protocol.IceRpc);
        this.AddScoped(provider =>
        {
            var serverOptions = provider.GetService<ServerOptions>() ?? new ServerOptions();
            if (provider.GetService<IDispatcher>() is IDispatcher dispatcher)
            {
                serverOptions.Dispatcher = dispatcher;
            }
            serverOptions.Endpoint = provider.GetRequiredService<Endpoint>();
            serverOptions.SimpleServerTransport =
                provider.GetRequiredService<IServerTransport<ISimpleNetworkConnection>>();
            serverOptions.MultiplexedServerTransport =
                provider.GetRequiredService<IServerTransport<IMultiplexedNetworkConnection>>();
            var server = new Server(serverOptions);
            server.Listen();
            return server;
        });

        this.AddScoped(provider =>
        {
            var connectionOptions = provider.GetService<ConnectionOptions>() ?? new ConnectionOptions();
            if (connectionOptions.RemoteEndpoint == null)
            {
                connectionOptions.RemoteEndpoint = provider.GetRequiredService<Server>().Endpoint;
            }
            connectionOptions.SimpleClientTransport =
                provider.GetRequiredService<IClientTransport<ISimpleNetworkConnection>>();
            connectionOptions.MultiplexedClientTransport =
                provider.GetRequiredService<IClientTransport<IMultiplexedNetworkConnection>>();
            return new Connection(connectionOptions);
        });
    }
}

public static class ConnectionServiceCollectionExtensions
{
    public static IServiceCollection UseDispatcher(
        this IServiceCollection serviceCollection,
        IDispatcher dispatcher) =>
        serviceCollection.AddScoped(_ => dispatcher);

    public static IServiceCollection UseConnectionOptions(
        this IServiceCollection serviceCollection,
        ConnectionOptions connectionOptions) =>
        serviceCollection.AddScoped(_ => connectionOptions);

    public static IServiceCollection UseServerOptions(
        this IServiceCollection serviceCollection,
        ServerOptions serverOptions) =>
        serviceCollection.AddScoped(_ => serverOptions);
}
