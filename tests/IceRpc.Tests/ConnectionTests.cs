// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Slice;
using IceRpc.Transports;
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

        IConnection? serverConnection = null;
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

    /// <summary>Verifies that aborting the connection aborts the invocations.</summary>
    [Test]
    public async Task Aborting_the_client_connection_aborts_the_invocations([Values("ice", "icerpc")] string protocol)
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
        connection.Abort();

        // Assert
        Assert.That(async () => await invokeTask, Throws.TypeOf<ConnectionAbortedException>());
    }

    /// <summary>Verifies that aborting the server connection aborts the invocations.</summary>
    [Test]
    public async Task Aborting_the_server_connection_aborts_the_invocations([Values("ice", "icerpc")] string protocol)
    {
        // Arrange
        using var start = new SemaphoreSlim(0);
        using var hold = new SemaphoreSlim(0);
        IConnection? serverConnection = null;
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
        ((Connection)serverConnection!).Abort();

        // Assert
        Assert.That(async () => await invokeTask, Throws.TypeOf<ConnectionLostException>());
    }

    /// <summary>Verifies that aborting the connection raises the connection closed event.</summary>
    [Test]
    public async Task Connection_closed_event(
        [Values("ice", "icerpc")] string protocol,
        [Values(true, false)] bool closeClientConnection)
    {
        // Arrange
        IConnection? serverConnection = null;
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
        (closeClientConnection ? clientConnection : (Connection)serverConnection!).Abort();

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
        var tcpServerTransportOptions = new TcpServerTransportOptions
        {
            IdleTimeout = TimeSpan.FromMilliseconds(500),
        };

        var tcpClientTransportOptions = new TcpClientTransportOptions
        {
            IdleTimeout = TimeSpan.FromMilliseconds(500),
        };

        // Arrange
        await using var provider = new ConnectionServiceCollection(protocol)
            .UseTcp(tcpServerTransportOptions, tcpClientTransportOptions)
            .UseDispatcher(new InlineDispatcher((request, cancel) => new(new OutgoingResponse(request))))
            .BuildServiceProvider();
        var server = provider.GetRequiredService<Server>();
        var connection = provider.GetRequiredService<Connection>();

        var proxy = Proxy.FromConnection(connection, "/foo");

        await proxy.Invoker.InvokeAsync(new OutgoingRequest(proxy));
        while (connection.State != ConnectionState.Closed)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        // Act/Assert
        Assert.That(
            async () => await connection.InvokeAsync(new OutgoingRequest(new Proxy(Protocol.IceRpc)), default),
            Throws.TypeOf<ConnectionClosedException>());
    }

    [Test]
    public async Task Resumable_connection_can_reconnect_after_being_idle([Values("ice", "icerpc")] string protocol)
    {
        var tcpServerTransportOptions = new TcpServerTransportOptions
        {
            IdleTimeout = TimeSpan.FromMilliseconds(500),
        };

        var tcpClientTransportOptions = new TcpClientTransportOptions
        {
            IdleTimeout = TimeSpan.FromMilliseconds(500),
        };

        // Arrange
        await using var provider = new ConnectionServiceCollection(protocol)
            .UseTcp(tcpServerTransportOptions, tcpClientTransportOptions)
            .UseDispatcher(new InlineDispatcher((request, cancel) => new(new OutgoingResponse(request))))
            .UseConnectionOptions(new ConnectionOptions { IsResumable = true })
            .BuildServiceProvider();
        var server = provider.GetRequiredService<Server>();
        var connection = provider.GetRequiredService<Connection>();

        var proxy = Proxy.FromConnection(connection, "/foo");

        await proxy.Invoker.InvokeAsync(new OutgoingRequest(proxy));

        // Act/Assert
        while (connection.State != ConnectionState.NotConnected)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
    }

    [Test]
    public async Task Resumable_connection_can_reconnect_after_gracefull_peer_shutdown(
        [Values("ice", "icerpc")] string protocol)
    {
        // Arrange
        Connection? serverConnection = null;
        await using var provider = new ConnectionServiceCollection(protocol)
            .UseDispatcher(new InlineDispatcher((request, cancel) =>
                {
                    serverConnection = (Connection)request.Connection;
                    return new(new OutgoingResponse(request));
                }))
            .UseConnectionOptions(new ConnectionOptions { IsResumable = true })
            .BuildServiceProvider();
        var server = provider.GetRequiredService<Server>();
        var connection = provider.GetRequiredService<Connection>();

        var proxy = Proxy.FromConnection(connection, "/foo");
        await proxy.Invoker.InvokeAsync(new OutgoingRequest(proxy));

        // Act
        await serverConnection!.ShutdownAsync();

        // Assert
        while (connection.State != ConnectionState.NotConnected)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
    }

    [Test]
    public async Task Resumable_connection_can_reconnect_after_peer_abort(
        [Values("ice", "icerpc")] string protocol)
    {
        // Arrange
        Connection? serverConnection = null;
        await using var provider = new ConnectionServiceCollection(protocol)
            .UseDispatcher(new InlineDispatcher((request, cancel) =>
                {
                    serverConnection = (Connection)request.Connection;
                    return new(new OutgoingResponse(request));
                }))
            .UseConnectionOptions(new ConnectionOptions { IsResumable = true })
            .BuildServiceProvider();
        var server = provider.GetRequiredService<Server>();
        var connection = provider.GetRequiredService<Connection>();

        var proxy = Proxy.FromConnection(connection, "/foo");
        await proxy.Invoker.InvokeAsync(new OutgoingRequest(proxy));

        // Act
        serverConnection!.Abort();

        // Assert
        while (connection.State != ConnectionState.NotConnected)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
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
        IConnection? serverConnection = null;
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
        var shutdownTask = (closeClientSide ? clientConnection : (Connection)serverConnection!)
            .ShutdownAsync(CancellationToken.None);

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
    public async Task Shutdown_does_not_throw_if_connect_fails()
    {
        // Arrange
        await using var connection = new Connection("icerpc://localhost");
        _ = connection.ConnectAsync();

        // Act/Assert
        Assert.DoesNotThrowAsync(() => connection.ShutdownAsync());
    }

    [Test]
    public async Task Dispose_does_not_throw_if_connect_fails()
    {
        // Arrange
        await using var connection = new Connection("icerpc://localhost");
        _ = connection.ConnectAsync();

        // Act/Assert
        Assert.DoesNotThrowAsync(async () => await connection.DisposeAsync());
    }

    [Test]
    public async Task Shutdown_cancellation(
        [Values("ice", "icerpc")] string protocol,
        [Values(true, false)] bool closeClientSide)
    {
        // Arrange
        using var start = new SemaphoreSlim(0);
        using var hold = new SemaphoreSlim(0);

        IConnection? serverConnection = null;
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
        var shutdownTask = (closeClientSide ? clientConnection : (Connection)serverConnection!)
            .ShutdownAsync(shutdownCancelationSource.Token);

        // Act
        shutdownCancelationSource.Cancel();

        // Assert
        if (closeClientSide)
        {
            Assert.That(async () => await shutdownTask, Throws.Nothing);

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

        IConnection? serverConnection = null;
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
        _ = (closeClientSide ? clientConnection : (Connection)serverConnection!).ShutdownAsync(default);

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
                Assert.ThrowsAsync<ConnectionAbortedException>(async () => await pingTask);
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
            serverOptions.IceServerOptions = new()
            {
                ServerTransport = provider.GetRequiredService<IServerTransport<ISimpleNetworkConnection>>()
            };
            serverOptions.IceRpcServerOptions = new()
            {
                ServerTransport = provider.GetRequiredService<IServerTransport<IMultiplexedNetworkConnection>>()
            };
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
            connectionOptions.IceClientOptions = new()
            {
                ClientTransport = provider.GetRequiredService<IClientTransport<ISimpleNetworkConnection>>()
            };
            connectionOptions.IceRpcClientOptions = new()
            {
                ClientTransport = provider.GetRequiredService<IClientTransport<IMultiplexedNetworkConnection>>()
            };
            return new Connection(connectionOptions);
        });
    }
}
