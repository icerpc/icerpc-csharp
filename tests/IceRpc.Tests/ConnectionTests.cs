// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slice;
using IceRpc.Tests.Common;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Tests;

[Parallelizable(ParallelScope.All)]
public class ConnectionTests
{
    /// <summary>Verifies that Server.ServerAddress and ClientConnection.ServerAddress's Transport property is set to "coloc".
    /// </summary>
    [Test]
    public async Task Coloc_server_address_gets_transport_property([Values("ice", "icerpc")] string protocol)
    {
        await using ServiceProvider provider = new ServiceCollection()
            .AddColocTest(ServiceNotFoundDispatcher.Instance, Protocol.FromString(protocol))
            .BuildServiceProvider(validateScopes: true);

        var server = provider.GetRequiredService<Server>();
        server.Listen();
        var connection = provider.GetRequiredService<ClientConnection>();

        Assert.That(server.ServerAddress.Transport, Is.EqualTo("coloc"));
        Assert.That(connection.ServerAddress.Transport, Is.EqualTo("coloc"));
    }

    /// <summary>Verifies that Server.ServerAddress and ClientConnection.ServerAddress's Transport property is set to "tcp".
    /// </summary>
    [Test]
    public async Task Tcp_server_address_gets_transport_property([Values("ice", "icerpc")] string protocol)
    {
        await using ServiceProvider provider = new ServiceCollection()
            .AddTcpTest(ServiceNotFoundDispatcher.Instance, Protocol.FromString(protocol))
            .BuildServiceProvider(validateScopes: true);

        var server = provider.GetRequiredService<Server>();
        server.Listen();
        var connection = provider.GetRequiredService<ClientConnection>();

        Assert.That(server.ServerAddress.Transport, Is.EqualTo("tcp"));
        Assert.That(connection.ServerAddress.Transport, Is.EqualTo("tcp"));
    }

    [Test]
    public async Task Coloc_ClientConnection_ServerAddress_has_transport_property([Values("ice", "icerpc")] string protocol)
    {
        await using ServiceProvider provider = new ServiceCollection()
            .AddColocTest(ServiceNotFoundDispatcher.Instance, Protocol.FromString(protocol))
            .BuildServiceProvider(validateScopes: true);

        var server = provider.GetRequiredService<Server>();
        server.Listen();
        var connection = provider.GetRequiredService<ClientConnection>();

        Assert.That(server.ServerAddress.Transport, Is.EqualTo("coloc"));
        Assert.That(connection.ServerAddress.Transport, Is.EqualTo("coloc"));
    }

    /// <summary>Verifies that aborting the connection aborts the invocations.</summary>
    [Test]
    public async Task Disposing_the_client_connection_aborts_the_invocations([Values("ice", "icerpc")] string protocol)
    {
        // Arrange
        using var dispatcher = new TestDispatcher();

        await using ServiceProvider provider = new ServiceCollection()
            .AddColocTest(dispatcher, Protocol.FromString(protocol))
            .BuildServiceProvider(validateScopes: true);

        var server = provider.GetRequiredService<Server>();
        server.Listen();
        var connection = provider.GetRequiredService<ClientConnection>();

        var serviceAddress = new ServiceAddress(connection.Protocol) { Path = "/foo" };

        var invokeTask = connection.InvokeAsync(new OutgoingRequest(serviceAddress));
        await dispatcher.DispatchStart; // Wait for dispatch to start

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
        using var dispatcher = new TestDispatcher();

        await using ServiceProvider provider = new ServiceCollection()
            .AddColocTest(dispatcher, Protocol.FromString(protocol))
            .BuildServiceProvider(validateScopes: true);

        var server = provider.GetRequiredService<Server>();
        server.Listen();
        var connection = provider.GetRequiredService<ClientConnection>();

        var serviceAddress = new ServiceAddress(connection.Protocol) { Path = "/foo" };

        var request = new OutgoingRequest(serviceAddress);
        var invokeTask = connection.InvokeAsync(request);
        var serverConnection = (ProtocolConnection)(await dispatcher.DispatchStart)!.Invoker;

        // Act
        await serverConnection.DisposeAsync();

        // Assert
        if (protocol == "ice")
        {
            Assert.That(
                async () =>
                {
                    IncomingResponse response = await invokeTask;
                    throw await response.DecodeFailureAsync(request, new ServiceProxy(connection, serviceAddress));
                },
                Throws.TypeOf<DispatchException>());
        }
        else
        {
            Assert.That(async () => await invokeTask, Throws.TypeOf<IceRpcProtocolStreamException>());
        }
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

        using var listener = slicServerTransport.Listen(
            new ServerAddress(new Uri("icerpc://127.0.0.1:0")),
            new MultiplexedConnectionOptions(),
            null);
        await using var connection = new ClientConnection(new ClientConnectionOptions
        {
            ServerAddress = listener.ServerAddress,
            ConnectTimeout = TimeSpan.FromMilliseconds(100)
        });

        // Assert
        Assert.That(
            async () => await connection.InvokeAsync(new OutgoingRequest(serviceAddress), default),
            Throws.TypeOf<TimeoutException>());
    }

    /// <summary>Verifies that InvokeAsync succeeds when there is a compatible server address.</summary>
    [TestCase("icerpc://testhost.com?transport=coloc")]
    [TestCase("icerpc://testhost.com:4062")]
    [TestCase("icerpc://testhost.com")]
    [TestCase("icerpc://foo.com/path?alt-server=testhost.com")]
    [TestCase("icerpc:/path")]
    [TestCase("ice://testhost.com:4061/path")]
    public async Task InvokeAsync_succeeds_with_a_compatible_server_address(ServiceAddress serviceAddress)
    {
        // Arrange
        await using ServiceProvider provider =
            new ServiceCollection()
                .AddColocTest(
                    new InlineDispatcher((request, cancel) => new(new OutgoingResponse(request))),
                    serviceAddress.Protocol!,
                    host: "testhost.com")
                .BuildServiceProvider(validateScopes: true);

        Server server = provider.GetRequiredService<Server>();
        ClientConnection connection = provider.GetRequiredService<ClientConnection>();

        server.Listen();

        // Assert
        Assert.That(
            async () => await connection.InvokeAsync(new OutgoingRequest(serviceAddress), default),
            Throws.Nothing);
    }

    /// <summary>Verifies that InvokeAsync fails when there is no compatible server address.</summary>
    [TestCase("icerpc://foo.com?transport=tcp", "icerpc://foo.com?transport=coloc")]
    [TestCase("icerpc://foo.com", "icerpc://foo.com?transport=coloc")]
    [TestCase("icerpc://foo.com", "icerpc://bar.com")]
    [TestCase("icerpc://foo.com", "icerpc://foo.com:10000")]
    [TestCase("icerpc://foo.com", "icerpc://foo.com?tanpot=tcp")]
    [TestCase("icerpc://foo.com", "icerpc://foo.com?t=10000")]
    [TestCase("ice://foo.com?t=10000&z", "ice://foo.com:10000/path?t=10000&z")]
    public async Task InvokeAsync_fails_without_a_compatible_server_address(
        ServerAddress serverAddress,
        ServiceAddress serviceAddress)
    {
        // Arrange
        await using var connection = new ClientConnection(serverAddress);

        // Assert
        Assert.That(
            async () => await connection.InvokeAsync(new OutgoingRequest(serviceAddress), default),
            Throws.TypeOf<InvalidOperationException>());
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
        ProtocolConnection? serverConnection = null;
        IServiceCollection services = new ServiceCollection().AddColocTest(
            new InlineDispatcher((request, cancel) =>
            {
                serverConnection = (ProtocolConnection)request.ConnectionContext.Invoker;
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
        ProtocolConnection? serverConnection = null;

        IServiceCollection services = new ServiceCollection().AddColocTest(
            new InlineDispatcher((request, cancel) =>
            {
                serverConnection = (ProtocolConnection)request.ConnectionContext.Invoker;
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
    public async Task Connect_returns_transport_connection_information([Values("ice", "icerpc")] string protocol)
    {
        // Arrange
        IServiceCollection services = new ServiceCollection().AddColocTest(
            new InlineDispatcher((request, cancel) => new(new OutgoingResponse(request))),
            Protocol.FromString(protocol));

        await using var provider = services.BuildServiceProvider(validateScopes: true);

        provider.GetRequiredService<Server>().Listen();
        var connection = provider.GetRequiredService<ClientConnection>();

        // Act
        TransportConnectionInformation transportConnectionInformation = await connection.ConnectAsync(default);

        // Assert
        Assert.That(transportConnectionInformation, Is.Not.EqualTo(new TransportConnectionInformation()));
    }

    [Test]
    public async Task Shutdown_connection(
        [Values("icerpc", "ice")] string protocol,
        [Values] bool closeClientSide)
    {
        // Arrange
        using var dispatcher = new TestDispatcher();

        await using ServiceProvider provider = new ServiceCollection()
            .AddColocTest(dispatcher, Protocol.FromString(protocol))
            .BuildServiceProvider(validateScopes: true);

        var server = provider.GetRequiredService<Server>();
        server.Listen();
        var clientConnection = provider.GetRequiredService<ClientConnection>();
        var proxy = new ServiceProxy(clientConnection, new Uri($"{protocol}:/path"));
        var pingTask = proxy.IcePingAsync();
        var serverConnection = (ProtocolConnection)(await dispatcher.DispatchStart)!.Invoker;

        // Act
        Task shutdownTask = closeClientSide ?
            clientConnection.ShutdownAsync(default) :
            serverConnection.ShutdownAsync("", default);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(dispatcher.ReleaseDispatch(), Is.EqualTo(0));
            Assert.That(async () => await shutdownTask, Throws.Nothing);
            Assert.That(async () => await pingTask, Throws.Nothing);
        });
    }

    [Test]
    public async Task Dispose_does_not_throw_if_connect_fails()
    {
        // Arrange
        await using var connection = new ClientConnection(new Uri("icerpc://localhost"));
        _ = connection.ConnectAsync();

        // Act/Assert
        Assert.DoesNotThrowAsync(async () => await connection.DisposeAsync());
    }

    [Test]
    public async Task Dispose_after_shutdown_abort_invocations_and_cancel_dispatches(
        [Values("ice", "icerpc")] string protocol,
        [Values(true, false)] bool closeClientSide)
    {
        // Arrange
        using var dispatcher = new TestDispatcher();

        await using ServiceProvider provider = new ServiceCollection()
            .AddColocTest(dispatcher, Protocol.FromString(protocol))
            .BuildServiceProvider(validateScopes: true);

        var server = provider.GetRequiredService<Server>();
        server.Listen();
        var clientConnection = provider.GetRequiredService<ClientConnection>();
        var proxy = new ServiceProxy(clientConnection, new Uri($"{protocol}:/path"));
        var pingTask = proxy.IcePingAsync();
        var serverConnection = (ProtocolConnection)(await dispatcher.DispatchStart)!.Invoker;
        Task shutdownTask = closeClientSide ? clientConnection.ShutdownAsync() : serverConnection.ShutdownAsync("");

        // Act
        if (closeClientSide)
        {
            await clientConnection.DisposeAsync();
        }
        else
        {
            await serverConnection.DisposeAsync();
        }

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(async () => await shutdownTask, Throws.Nothing);
            Assert.That(async () => await dispatcher.DispatchComplete, Throws.InstanceOf<OperationCanceledException>());
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
    public async Task Shutdown_waits_for_connection_establishment([Values("ice", "icerpc")] string protocol)
    {
        // Arrange

        await using ServiceProvider provider = new ServiceCollection()
            .AddColocTest(ServiceNotFoundDispatcher.Instance, Protocol.FromString(protocol))
            .BuildServiceProvider(validateScopes: true);

        var server = provider.GetRequiredService<Server>();
        server.Listen();
        var connection = provider.GetRequiredService<ClientConnection>();

        Task connectTask = connection.ConnectAsync();

        // Act
        await connection.ShutdownAsync();

        // Assert
        Assert.That(connectTask.IsCompletedSuccessfully, Is.True);
    }

    [Test]
    public async Task Shutdown_timeout(
        [Values("ice", "icerpc")] string protocol,
        [Values] bool closeClientSide)
    {
        // Arrange
        using var dispatcher = new TestDispatcher();

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
        var proxy = new ServiceProxy(clientConnection, new Uri($"{protocol}:/path"));
        var pingTask = proxy.IcePingAsync();
        var serverConnection = (ProtocolConnection)(await dispatcher.DispatchStart).Invoker;

        // Act
        Task shutdownTask;
        if (closeClientSide)
        {
            shutdownTask = clientConnection.ShutdownAsync();
        }
        else
        {
            shutdownTask = serverConnection.ShutdownAsync("");
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
            // The ping can fail with either ConnectionLostException or IceRpcProtocolStreamException
            Exception? exception = Assert.CatchAsync<Exception>(async () => await pingTask);
            Assert.That(
                exception,
                Is.InstanceOf<ConnectionLostException>());
        }
    }
}
