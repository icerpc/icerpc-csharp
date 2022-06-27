// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slice;
using IceRpc.Tests.Common;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace IceRpc.Tests;

[Parallelizable(ParallelScope.All)]
public class ConnectionTests
{
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

        await using ServiceProvider provider = new ServiceCollection()
            .AddColocTest(dispatcher, Protocol.FromString(protocol))
            .BuildServiceProvider(validateScopes: true);

        var server = provider.GetRequiredService<Server>();
        server.Listen();
        var connection = provider.GetRequiredService<ClientConnection>();

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
        ServerConnection? serverConnection = null;
        var dispatcher = new InlineDispatcher(async (request, cancel) =>
        {
            serverConnection = (ServerConnection)request.Connection;
            start.Release();
            await hold.WaitAsync(cancel);
            return new OutgoingResponse(request);
        });

        await using ServiceProvider provider = new ServiceCollection()
            .AddColocTest(dispatcher, Protocol.FromString(protocol))
            .BuildServiceProvider(validateScopes: true);

        var server = provider.GetRequiredService<Server>();
        server.Listen();
        var connection = provider.GetRequiredService<ClientConnection>();

        var proxy = Proxy.FromConnection(connection, "/foo");

        var invokeTask = proxy.Invoker.InvokeAsync(new OutgoingRequest(proxy));
        await start.WaitAsync(); // Wait for dispatch to start

        // Act
        serverConnection!.Abort(); // TODO: move Abort to IConnection?

        // Assert
        Assert.That(async () => await invokeTask, Throws.TypeOf<ConnectionLostException>());
    }

    /// <summary>Verifies that aborting the connection executes the OnClose callback.</summary>
    [Test]
    public async Task Connection_closed_event(
        [Values("ice", "icerpc")] string protocol,
        [Values(true, false)] bool closeClientConnection)
    {
        // Arrange
        ServerConnection? serverConnection = null;
        var serverConnectionClosed = new TaskCompletionSource<object?>();
        var dispatcher = new InlineDispatcher((request, cancel) =>
        {
            serverConnection = (ServerConnection)request.Connection;
            serverConnection.OnClose(_ => serverConnectionClosed.SetResult(null));
            return new(new OutgoingResponse(request));
        });

        var clientConnectionClosed = new TaskCompletionSource<object?>();

        IServiceCollection services = new ServiceCollection().AddColocTest(dispatcher, Protocol.FromString(protocol));

        await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
        var server = provider.GetRequiredService<Server>();
        server.Listen();
        var clientConnection = provider.GetRequiredService<ClientConnection>();
        clientConnection.OnClose(_ => clientConnectionClosed.SetResult(null));

        var proxy = Proxy.FromConnection(clientConnection, "/foo");

        await proxy.Invoker.InvokeAsync(new OutgoingRequest(proxy));

        // Act
        if (closeClientConnection)
        {
            clientConnection.Abort();
        }
        else
        {
            serverConnection!.Abort();
        }

        // Assert
        Assert.That(async () => await serverConnectionClosed.Task, Throws.Nothing);
        Assert.That(async () => await clientConnectionClosed.Task, Throws.Nothing);
    }

    /// <summary>Verifies that connect establishment timeouts after the <see cref="ConnectionOptions.ConnectTimeout"/>
    /// time period.</summary>
    [Test]
    public async Task Connect_timeout()
    {
        // Arrange
        var tcpServerTransport = new TcpServerTransport();
        var slicServerTransport = new SlicServerTransport(tcpServerTransport);

        var proxy = new Proxy(Protocol.IceRpc);

        using var listener = slicServerTransport.Listen("icerpc://127.0.0.1:0", null, NullLogger.Instance);
        await using var connection = new ClientConnection(new ClientConnectionOptions
        {
            RemoteEndpoint = listener.Endpoint,
            ConnectTimeout = TimeSpan.FromMilliseconds(100),
        });

        // Assert
        Assert.That(
            async () => await connection.InvokeAsync(new OutgoingRequest(proxy), default),
            Throws.TypeOf<TimeoutException>());
    }

    [Test]
    public async Task Non_resumable_connection_cannot_reconnect([Values("ice", "icerpc")] string protocol)
    {
        // Arrange
        IServiceCollection services = new ServiceCollection();

        services
            .AddOptions<ClientConnectionOptions>()
            .Configure(options => options.IdleTimeout = TimeSpan.FromMilliseconds(500));

        services
            .AddOptions<ServerOptions>()
            .Configure(options => options.ConnectionOptions.IdleTimeout = TimeSpan.FromMilliseconds(500));

        await using ServiceProvider provider = services
            .AddTcpTest(
                new InlineDispatcher((request, cancel) => new(new OutgoingResponse(request))),
                Protocol.FromString(protocol))
            .BuildServiceProvider(validateScopes: true);

        var server = provider.GetRequiredService<Server>();
        server.Listen();
        var connection = provider.GetRequiredService<ClientConnection>();

        var proxy = Proxy.FromConnection(connection, "/foo");

        await proxy.Invoker.InvokeAsync(new OutgoingRequest(proxy));

        using var semaphore = new SemaphoreSlim(0);
        connection.OnClose(exception => semaphore.Release(1));
        await semaphore.WaitAsync();

        // Act/Assert
        Assert.That(
            async () => await connection.InvokeAsync(new OutgoingRequest(new Proxy(Protocol.IceRpc)), default),
            Throws.TypeOf<ConnectionClosedException>());
    }

    [Test]
    public async Task Resumable_connection_can_reconnect_after_being_idle([Values("ice", "icerpc")] string protocol)
    {
        // Arrange
        IServiceCollection services = new ServiceCollection();

        services.AddTcpTest(
            new InlineDispatcher((request, cancel) => new(new OutgoingResponse(request))),
            Protocol.FromString(protocol));

        services.AddIceRpcResumableClientConnection(); // overwrites AddIceRpcClientConnection from AddTcpTest

        services
            .AddOptions<ClientConnectionOptions>()
            .Configure(options => options.IdleTimeout = TimeSpan.FromMilliseconds(500));

        await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

        var server = provider.GetRequiredService<Server>();
        server.Listen();
        var connection = provider.GetRequiredService<ResumableClientConnection>();

        var proxy = Proxy.FromConnection(connection, "/foo");

        await proxy.Invoker.InvokeAsync(new OutgoingRequest(proxy));

        using var semaphore = new SemaphoreSlim(0);
        connection.OnDisconnect(exception =>
        {
            try
            {
                semaphore.Release(1);
            }
            catch (ObjectDisposedException)
            {
                // expected
            }
        });
        await semaphore.WaitAsync();

        // Act/Assert
        await proxy.Invoker.InvokeAsync(new OutgoingRequest(proxy));
    }

    [Test]
    public async Task Resumable_connection_can_reconnect_after_graceful_peer_shutdown(
        [Values("ice", "icerpc")] string protocol)
    {
        // Arrange
        ServerConnection? serverConnection = null;
        IServiceCollection services = new ServiceCollection().AddColocTest(
            new InlineDispatcher((request, cancel) =>
            {
                serverConnection = (ServerConnection)request.Connection;
                return new(new OutgoingResponse(request));
            }),
            Protocol.FromString(protocol));

        services.AddIceRpcResumableClientConnection(); // overwrites AddIceRpcClientConnection from AddColocTest

        await using var provider = services.BuildServiceProvider(validateScopes: true);
        var server = provider.GetRequiredService<Server>();
        server.Listen();
        var connection = provider.GetRequiredService<ResumableClientConnection>();

        var proxy = Proxy.FromConnection(connection, "/foo");
        await proxy.Invoker.InvokeAsync(new OutgoingRequest(proxy));

        using var semaphore = new SemaphoreSlim(0);
        connection.OnDisconnect(exception =>
        {
            try
            {
                semaphore.Release(1);
            }
            catch (ObjectDisposedException)
            {
                // expected
            }
        });
        await serverConnection!.ShutdownAsync();
        await semaphore.WaitAsync();

        // Act/Assert
        await proxy.Invoker.InvokeAsync(new OutgoingRequest(proxy));
    }

    [Test]
    public async Task Resumable_connection_can_reconnect_after_peer_abort(
        [Values("ice", "icerpc")] string protocol)
    {
        // Arrange
        ServerConnection? serverConnection = null;

        IServiceCollection services = new ServiceCollection().AddColocTest(
            new InlineDispatcher((request, cancel) =>
            {
                serverConnection = (ServerConnection)request.Connection;
                return new(new OutgoingResponse(request));
            }),
            Protocol.FromString(protocol));

        services.AddIceRpcResumableClientConnection(); // overwrites AddIceRpcClientConnection from AddColocTest

        await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

        provider.GetRequiredService<Server>().Listen();
        var connection = provider.GetRequiredService<ResumableClientConnection>();

        var proxy = Proxy.FromConnection(connection, "/foo");
        await proxy.Invoker.InvokeAsync(new OutgoingRequest(proxy));

        using var semaphore = new SemaphoreSlim(0);
        connection.OnDisconnect(exception =>
        {
            try
            {
                semaphore.Release(1);
            }
            catch (ObjectDisposedException)
            {
                // expected
            }
        });
        serverConnection!.Abort();
        await semaphore.WaitAsync();

        // Act/Assert
        await proxy.Invoker.InvokeAsync(new OutgoingRequest(proxy));
    }

    [Test]
    public async Task Resumable_connection_becomes_non_resumable_after_shutdown()
    {
        // Arrange
        IServiceCollection services = new ServiceCollection().AddColocTest(
            new InlineDispatcher((request, cancel) => new(new OutgoingResponse(request))),
            Protocol.IceRpc
        );

        services.AddIceRpcResumableClientConnection(); // overwrites AddIceRpcClientConnection from AddColocTest

        await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

        provider.GetRequiredService<Server>().Listen();
        var connection = provider.GetRequiredService<ResumableClientConnection>();
        var proxy = Proxy.FromConnection(connection, "/foo");

        await proxy.Invoker.InvokeAsync(new OutgoingRequest(proxy));

        // Act
        await connection.ShutdownAsync();

        // Assert
        Assert.That(connection.IsResumable, Is.False);
    }

    [Test]
    public async Task Connect_sets_network_connection_information([Values("ice", "icerpc")] string protocol)
    {
        // Arrange
        IServiceCollection services = new ServiceCollection().AddColocTest(
            new InlineDispatcher((request, cancel) => new(new OutgoingResponse(request))),
            Protocol.FromString(protocol));

        await using var provider = services.BuildServiceProvider(validateScopes: true);

        provider.GetRequiredService<Server>().Listen();
        var connection = provider.GetRequiredService<ClientConnection>();
        var networkConnectionInformation = connection.NetworkConnectionInformation;

        // Act
        await connection.ConnectAsync(default);

        // Assert
        Assert.That(networkConnectionInformation, Is.Null);
        Assert.That(connection.NetworkConnectionInformation, Is.Not.Null);
    }

    [Test]
    public async Task Shutdown_connection(
        [Values("icerpc", "ice")] string protocol,
        [Values] bool closeClientSide)
    {
        // Arrange
        using var start = new SemaphoreSlim(0);
        using var hold = new SemaphoreSlim(0);
        ServerConnection? serverConnection = null;
        var dispatcher = new InlineDispatcher(async (request, cancel) =>
        {
            serverConnection = (ServerConnection)request.Connection;
            start.Release();
            await hold.WaitAsync(CancellationToken.None);
            return new OutgoingResponse(request);
        });

        await using ServiceProvider provider = new ServiceCollection()
            .AddColocTest(dispatcher, Protocol.FromString(protocol))
            .BuildServiceProvider(validateScopes: true);

        var server = provider.GetRequiredService<Server>();
        server.Listen();
        var clientConnection = provider.GetRequiredService<ClientConnection>();
        var proxy = ServicePrx.FromConnection(clientConnection, "/path");
        var pingTask = proxy.IcePingAsync();
        await start.WaitAsync();

        // Act
        Task shutdownTask = closeClientSide ?
            clientConnection.ShutdownAsync(default) :
            serverConnection!.ShutdownAsync(default);

        // Assert
        if (closeClientSide && protocol == "ice")
        {
            await shutdownTask;

            // With the Ice protocol, when closing the connection with a pending invocation, invocations are
            // canceled immediately. The Ice protocol doesn't support reliably waiting for the response.
            // TODO: throwing OperationCanceledException is not correct.
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
    public async Task Dispose_does_not_throw_if_connect_fails()
    {
        // Arrange
        await using var connection = new ClientConnection("icerpc://localhost");
        _ = connection.ConnectAsync();

        // Act/Assert
        Assert.DoesNotThrowAsync(async () => await connection.DisposeAsync());
    }

    [Test]
    [Ignore("pending IProtocolConnection update")]
    public async Task Shutdown_cancellation(
        [Values("ice", "icerpc")] string protocol,
        [Values(true, false)] bool closeClientSide)
    {
        // Arrange
        using var start = new SemaphoreSlim(0);
        using var hold = new SemaphoreSlim(0);

        ServerConnection? serverConnection = null;
        using var shutdownCancellationSource = new CancellationTokenSource();
        var dispatchCompletionSource = new TaskCompletionSource();
        var dispatcher = new InlineDispatcher(async (request, cancel) =>
        {
            try
            {
                serverConnection = (ServerConnection)request.Connection;
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

        await using ServiceProvider provider = new ServiceCollection()
            .AddColocTest(dispatcher, Protocol.FromString(protocol))
            .BuildServiceProvider(validateScopes: true);

        var server = provider.GetRequiredService<Server>();
        server.Listen();
        var clientConnection = provider.GetRequiredService<ClientConnection>();
        var proxy = ServicePrx.FromConnection(clientConnection, "/path");
        var pingTask = proxy.IcePingAsync();
        await start.WaitAsync();
        Task shutdownTask = closeClientSide ?
            clientConnection.ShutdownAsync(shutdownCancellationSource.Token) :
            serverConnection!.ShutdownAsync(shutdownCancellationSource.Token);

        // Act
        shutdownCancellationSource.Cancel();

        // Assert
        if (closeClientSide)
        {
            Assert.That(async () => await shutdownTask, Throws.Nothing);

            Assert.That(async () => await pingTask, Throws.InstanceOf<OperationCanceledException>());
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
                Assert.That(async () => await pingTask, Throws.TypeOf<IceRpcProtocolStreamException>());
            }
        }
    }

    [Test]
    public async Task Shutdown_waits_for_connection_establishment([Values("ice", "icerpc")] string protocol)
    {
        // Arrange
        var tcpServerTransport = new TcpServerTransport();
        using var listener = tcpServerTransport.Listen(
            "icerpc://127.0.0.1:0",
            authenticationOptions: null,
            NullLogger.Instance);

        var endpoint = new Endpoint(Protocol.FromString(protocol))
        {
            Host = listener.Endpoint.Host,
            Port = listener.Endpoint.Port
        };

        await using var connection = new ClientConnection(new ClientConnectionOptions
        {
            RemoteEndpoint = endpoint,
        });
        Task connectTask = connection.ConnectAsync();

        // Act
        Task shutdownTask = connection.ShutdownAsync();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(connectTask.IsCompleted, Is.False);
            Assert.That(shutdownTask.IsCompleted, Is.False);
        });
        connection.Abort();
        Assert.Multiple(() =>
        {
            Assert.That(async () => await connectTask, Throws.TypeOf<ConnectionAbortedException>());
            Assert.That(async () => await shutdownTask, Throws.TypeOf<ConnectionAbortedException>());
        });
    }

    [Test]
    public async Task Shutdown_timeout(
        [Values("ice", "icerpc")] string protocol,
        [Values] bool closeClientSide)
    {
        // Arrange
        using var start = new SemaphoreSlim(0);
        using var hold = new SemaphoreSlim(0);

        ServerConnection? serverConnection = null;
        var dispatcher = new InlineDispatcher(async (request, cancel) =>
        {
            serverConnection = (ServerConnection)request.Connection;
            start.Release();
            await hold.WaitAsync(cancel);
            return new OutgoingResponse(request);
        });

        IServiceCollection services = new ServiceCollection().AddColocTest(dispatcher, Protocol.FromString(protocol));

        services
            .AddOptions<ClientConnectionOptions>()
            .Configure(
                options => options.ShutdownTimeout = closeClientSide ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(60));

        services
            .AddOptions<ServerOptions>()
            .Configure(
                options => options.ConnectionOptions.ShutdownTimeout =
                    closeClientSide ? TimeSpan.FromSeconds(60) : TimeSpan.FromSeconds(1));

        await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

        var server = provider.GetRequiredService<Server>();
        server.Listen();
        var clientConnection = provider.GetRequiredService<ClientConnection>();
        var proxy = ServicePrx.FromConnection(clientConnection, "/path");
        var pingTask = proxy.IcePingAsync();
        await start.WaitAsync();

        // Act
        if (closeClientSide)
        {
            _ = clientConnection.DisposeAsync().AsTask();
        }
        else
        {
            _ = serverConnection!.DisposeAsync().AsTask();
        }

        // Assert
        if (closeClientSide)
        {
            // TODO: not correct
            Assert.That(async () => await pingTask, Throws.InstanceOf<OperationCanceledException>());
        }
        else
        {
            if (protocol == "ice")
            {
                // Shutdown should trigger the abort of the connection on the client side after the shutdown timeout
                Assert.That(async () => await pingTask, Throws.InstanceOf<DispatchException>());
            }
            else
            {
                Assert.That(async () => await pingTask, Throws.InstanceOf<IceRpcProtocolStreamException>());
            }
        }
        hold.Release();
    }
}
