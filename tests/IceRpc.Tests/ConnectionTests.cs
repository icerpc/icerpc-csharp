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
    public async Task Disposing_the_client_connection_aborts_the_invocations([Values("ice", "icerpc")] string protocol)
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

        var serviceAddress = new ServiceAddress(connection.Protocol) { Path = "/foo" };

        var invokeTask = connection.InvokeAsync(new OutgoingRequest(serviceAddress));
        await start.WaitAsync(); // Wait for dispatch to start

        // Act
        await connection.DisposeAsync();

        // Assert
        Assert.That(async () => await invokeTask, Throws.TypeOf<ConnectionAbortedException>());
    }

    /// <summary>Verifies that aborting the server connection aborts the invocations.</summary>
    [Test]
    public async Task Disposing_the_server_connection_aborts_the_invocations([Values("ice", "icerpc")] string protocol)
    {
        // Arrange
        using var start = new SemaphoreSlim(0);
        using var hold = new SemaphoreSlim(0);
        IProtocolConnection? serverConnection = null;
        var dispatcher = new InlineDispatcher(async (request, cancel) =>
        {
            serverConnection = (IProtocolConnection)request.Invoker;
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

        var serviceAddress = new ServiceAddress(connection.Protocol) { Path = "/foo" };

        var request = new OutgoingRequest(serviceAddress);
        var invokeTask = connection.InvokeAsync(request);
        await start.WaitAsync(); // Wait for dispatch to start

        // Act
        await serverConnection!.DisposeAsync();

        // Assert
        if (protocol == "ice")
        {
            Assert.That(
                async () =>
                {
                    IncomingResponse response = await invokeTask;
                    throw await response.DecodeFailureAsync(request);
                },
                Throws.TypeOf<DispatchException>());
        }
        else
        {
            Assert.That(async () => await invokeTask, Throws.TypeOf<IceRpcProtocolStreamException>());
        }
    }

    /// <summary>Verifies that disposing the connection executes the OnAbort callback.</summary>
    [Test]
    public async Task Connection_abort_callback(
        [Values("ice", "icerpc")] string protocol,
        [Values(true, false)] bool abortClientConnection)
    {
        // Arrange
        IProtocolConnection? serverConnection = null;
        var dispatcher = new InlineDispatcher((request, cancel) =>
        {
            serverConnection = (IProtocolConnection)request.Invoker;
            return new(new OutgoingResponse(request));
        });

        IServiceCollection services = new ServiceCollection().AddColocTest(dispatcher, Protocol.FromString(protocol));

        await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
        var server = provider.GetRequiredService<Server>();
        server.Listen();
        var clientConnection = provider.GetRequiredService<ClientConnection>();

        var serviceAddress = new ServiceAddress(clientConnection.Protocol) { Path = "/foo" };

        await clientConnection.InvokeAsync(new OutgoingRequest(serviceAddress));

        var onAbortCalled = new TaskCompletionSource<object?>();
        if (abortClientConnection)
        {
            serverConnection!.OnAbort(exception => onAbortCalled.SetException(exception));
            try
            {
                await clientConnection.ShutdownAsync(new CancellationToken(true));
            }
            catch (OperationCanceledException)
            {
            }
        }
        else
        {
            clientConnection.OnAbort(exception => onAbortCalled.SetException(exception));
            try
            {
                await serverConnection!.ShutdownAsync("", new CancellationToken(true));
            }
            catch (OperationCanceledException)
            {
            }
        }

        // Act
        if (abortClientConnection)
        {
            await clientConnection.DisposeAsync();
        }
        else
        {
            await serverConnection!.DisposeAsync();
        }

        // Assert
        Assert.That(async () => await onAbortCalled.Task, Throws.InstanceOf<ConnectionLostException>());
    }

    /// <summary>Verifies that connect establishment timeouts after the <see cref="ConnectionOptions.ConnectTimeout"/>
    /// time period.</summary>
    [Test]
    public async Task Connect_timeout()
    {
        // Arrange
        var tcpServerTransport = new TcpServerTransport();
        var slicServerTransport = new SlicServerTransport(tcpServerTransport);

        var serviceAddress = new ServiceAddress(Protocol.IceRpc);

        using var listener = slicServerTransport.Listen("icerpc://127.0.0.1:0", null, NullLogger.Instance);
        await using var connection = new ClientConnection(new ClientConnectionOptions
        {
            RemoteEndpoint = listener.Endpoint,
            ConnectTimeout = TimeSpan.FromMilliseconds(100)
        });

        // Assert
        Assert.That(
            async () => await connection.InvokeAsync(new OutgoingRequest(serviceAddress), default),
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

        var serviceAddress = new ServiceAddress(connection.Protocol) { Path = "/foo" };

        await connection.InvokeAsync(new OutgoingRequest(serviceAddress));

        using var semaphore = new SemaphoreSlim(0);
        connection.OnShutdown(message => semaphore.Release(1));
        await semaphore.WaitAsync();

        // Act/Assert
        Assert.That(
            async () => await connection.InvokeAsync(new OutgoingRequest(new ServiceAddress(Protocol.IceRpc)), default),
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

        var serviceAddress = new ServiceAddress(connection.Protocol) { Path = "/foo" };

        await connection.InvokeAsync(new OutgoingRequest(serviceAddress));

        using var semaphore = new SemaphoreSlim(0);
        connection.OnShutdown(message =>
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
        await connection.InvokeAsync(new OutgoingRequest(serviceAddress));
    }

    [Test]
    public async Task Resumable_connection_can_reconnect_after_graceful_peer_shutdown(
        [Values("icerpc", "ice")] string protocol)
    {
        // Arrange
        IProtocolConnection? serverConnection = null;
        IServiceCollection services = new ServiceCollection().AddColocTest(
            new InlineDispatcher((request, cancel) =>
            {
                serverConnection = (IProtocolConnection)request.Invoker;
                return new(new OutgoingResponse(request));
            }),
            Protocol.FromString(protocol));

        services.AddIceRpcResumableClientConnection(); // overwrites AddIceRpcClientConnection from AddColocTest

        await using var provider = services.BuildServiceProvider(validateScopes: true);
        var server = provider.GetRequiredService<Server>();
        server.Listen();
        var connection = provider.GetRequiredService<ResumableClientConnection>();

        var serviceAddress = new ServiceAddress(connection.Protocol) { Path = "/foo" };
        await connection.InvokeAsync(new OutgoingRequest(serviceAddress));

        using var semaphore = new SemaphoreSlim(0);

        connection.OnShutdown(message =>
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

        await serverConnection!.ShutdownAsync("");
        await semaphore.WaitAsync();

        // Act/Assert
        await connection.InvokeAsync(new OutgoingRequest(serviceAddress));
    }

    [Test]
    public async Task Resumable_connection_can_reconnect_after_peer_abort(
        [Values("ice", "icerpc")] string protocol)
    {
        // Arrange
        IProtocolConnection? serverConnection = null;

        IServiceCollection services = new ServiceCollection().AddColocTest(
            new InlineDispatcher((request, cancel) =>
            {
                serverConnection = (IProtocolConnection)request.Invoker;
                return new(new OutgoingResponse(request));
            }),
            Protocol.FromString(protocol));

        services.AddIceRpcResumableClientConnection(); // overwrites AddIceRpcClientConnection from AddColocTest

        await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

        provider.GetRequiredService<Server>().Listen();
        var connection = provider.GetRequiredService<ResumableClientConnection>();

        var serviceAddress = new ServiceAddress(connection.Protocol) { Path = "/foo" };
        await connection.InvokeAsync(new OutgoingRequest(serviceAddress));

        using var semaphore = new SemaphoreSlim(0);
        connection.OnAbort(exception =>
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
        try
        {
            await serverConnection!.ShutdownAsync("", new CancellationToken(true));
        }
        catch
        {
        }
        await serverConnection!.DisposeAsync();
        await semaphore.WaitAsync();

        // Act/Assert
        await connection.InvokeAsync(new OutgoingRequest(serviceAddress));
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
        var serviceAddress = new ServiceAddress(connection.Protocol) { Path = "/foo" };

        await connection.InvokeAsync(new OutgoingRequest(serviceAddress));

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
        Assert.Multiple(() =>
        {
            Assert.That(networkConnectionInformation, Is.Null);
            Assert.That(connection.NetworkConnectionInformation, Is.Not.Null);
        });
    }

    [Test]
    public async Task Shutdown_connection(
        [Values("icerpc", "ice")] string protocol,
        [Values] bool closeClientSide)
    {
        // Arrange
        using var start = new SemaphoreSlim(0);
        using var hold = new SemaphoreSlim(0);
        IProtocolConnection? serverConnection = null;
        var dispatcher = new InlineDispatcher(async (request, cancel) =>
        {
            serverConnection = (IProtocolConnection)request.Invoker;
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
        var serviceAddress = new ServiceProxy(clientConnection, "/path", clientConnection.Protocol);
        var pingTask = serviceAddress.IcePingAsync();
        await start.WaitAsync();

        // Act
        Task shutdownTask = closeClientSide ?
            clientConnection.ShutdownAsync(default) :
            serverConnection!.ShutdownAsync("", default);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(hold.Release(), Is.EqualTo(0));
            Assert.That(async () => await shutdownTask, Throws.Nothing);
            Assert.That(async () => await pingTask, Throws.Nothing);
        });
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
    [Repeat(100)]
    public async Task Dispose_after_shutdown_abort_invocations_and_cancel_dispatches(
        [Values("ice", "icerpc")] string protocol,
        [Values(true, false)] bool closeClientSide)
    {
        // Arrange
        using var start = new SemaphoreSlim(0);
        using var hold = new SemaphoreSlim(0);

        IProtocolConnection? serverConnection = null;
        using var shutdownCancellationSource = new CancellationTokenSource();
        var dispatchCompletionSource = new TaskCompletionSource();
        var dispatcher = new InlineDispatcher(async (request, cancel) =>
        {
            try
            {
                serverConnection = (IProtocolConnection)request.Invoker;
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
        var serviceAddress = new ServiceProxy(clientConnection, "/path", clientConnection.Protocol);
        var pingTask = serviceAddress.IcePingAsync();
        await start.WaitAsync();
        Task shutdownTask = closeClientSide ? clientConnection.ShutdownAsync() : serverConnection!.ShutdownAsync("");

        // Act
        if (closeClientSide)
        {
            await clientConnection.DisposeAsync();
        }
        else
        {
            await serverConnection!.DisposeAsync();
        }

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(async () => await shutdownTask, Throws.Nothing);
            Assert.That(async () => await dispatchCompletionSource.Task, Throws.Nothing);
            if (closeClientSide)
            {
                Assert.That(async () => await pingTask, Throws.InstanceOf<ConnectionAbortedException>());
            }
            else
            {
                if (protocol == "ice")
                {
                    Assert.That(async () => await pingTask, Throws.TypeOf<DispatchException>());
                }
                else
                {
                    Assert.That(async () => await pingTask, Throws.TypeOf<IceRpcProtocolStreamException>());
                }
            }
        });
    }

    [Test]
    [Ignore("does not work")]
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
        await connection.DisposeAsync();
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

        IProtocolConnection? serverConnection = null;
        IDispatcher dispatcher = new InlineDispatcher(async (request, cancel) =>
        {
            serverConnection = (IProtocolConnection)request.Invoker;
            start.Release();
            await hold.WaitAsync(cancel);
            return new OutgoingResponse(request);
        });

        IServiceCollection services = new ServiceCollection().AddColocTest(dispatcher, Protocol.FromString(protocol));

        services
            .AddOptions<ClientConnectionOptions>()
            .Configure(
                options => options.ShutdownTimeout =
                    closeClientSide ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(60));

        services
            .AddOptions<ServerOptions>()
            .Configure(
                options => options.ConnectionOptions.ShutdownTimeout =
                    closeClientSide ? TimeSpan.FromSeconds(60) : TimeSpan.FromSeconds(1));

        await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

        var server = provider.GetRequiredService<Server>();
        server.Listen();
        var clientConnection = provider.GetRequiredService<ClientConnection>();
        var serviceAddress = new ServiceProxy(clientConnection, "/path", clientConnection.Protocol);
        var pingTask = serviceAddress.IcePingAsync();
        await start.WaitAsync();

        // Act
        Task shutdownTask;
        if (closeClientSide)
        {
            shutdownTask = clientConnection.ShutdownAsync();
        }
        else
        {
            shutdownTask = serverConnection!.ShutdownAsync("");
        }

        // Assert
        Assert.That(async () => await shutdownTask, Throws.InstanceOf<TimeoutException>());
        if (closeClientSide)
        {
            await clientConnection.DisposeAsync();
            Assert.That(async () => await pingTask, Throws.InstanceOf<ConnectionAbortedException>());
        }
        else
        {
            await serverConnection!.DisposeAsync();
            Assert.That(async () => await pingTask, Throws.InstanceOf<ConnectionLostException>());
        }
        hold.Release();
    }
}
