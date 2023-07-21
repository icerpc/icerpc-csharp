// Copyright (c) ZeroC, Inc.

using IceRpc.Internal; // ServiceNotFoundDispatcher
using IceRpc.Tests.Common;
using IceRpc.Transports;
using IceRpc.Transports.Coloc;
using IceRpc.Transports.Slic;
using IceRpc.Transports.Tcp;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Tests;

[Parallelizable(ParallelScope.All)]
public class ClientConnectionTests
{
    private static List<Protocol> Protocols => new() { Protocol.IceRpc, Protocol.Ice };

    /// <summary>Verifies that <see cref="ClientConnection.ConnectAsync" /> returns a valid <see
    /// cref="TransportConnectionInformation" /></summary>
    [Test, TestCaseSource(nameof(Protocols))]
    public async Task Connect_returns_transport_connection_information(Protocol protocol)
    {
        // Arrange
        await using var server = new Server(
            new ServerOptions()
            {
                ConnectionOptions = new ConnectionOptions()
                {
                    Dispatcher = ServiceNotFoundDispatcher.Instance,
                },
                ServerAddress = new ServerAddress(new Uri($"{protocol}://127.0.0.1:0"))
            },
            multiplexedServerTransport: new SlicServerTransport(new TcpServerTransport()),
            duplexServerTransport: new TcpServerTransport());
        ServerAddress serverAddress = server.Listen();

        await using var connection = new ClientConnection(
            new ClientConnectionOptions() { ServerAddress = serverAddress },
            duplexClientTransport: new TcpClientTransport(),
            multiplexedClientTransport: new SlicClientTransport(new TcpClientTransport()));

        // Act
        TransportConnectionInformation transportConnectionInformation = await connection.ConnectAsync();

        // Assert
        Assert.That(transportConnectionInformation.LocalNetworkAddress, Is.Not.Null);
        Assert.That(transportConnectionInformation.RemoteNetworkAddress, Is.Not.Null);
    }

    [Test]
    public async Task Connect_times_out_after_connect_timeout()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddOptions<ClientConnectionOptions>().Configure(
            options => options.ConnectTimeout = TimeSpan.FromMilliseconds(300));
        await using ServiceProvider provider =
            services
                .AddClientServerColocTest(dispatcher: ServiceNotFoundDispatcher.Instance)
                .AddTestDuplexTransportDecorator(
                    serverOperationsOptions: new()
                    {
                        Hold = DuplexTransportOperations.Connect
                    })
                .BuildServiceProvider(validateScopes: true);

        Server server = provider.GetRequiredService<Server>();
        server.Listen();
        ClientConnection sut = provider.GetRequiredService<ClientConnection>();

        // Act/Assert
        Assert.That(() => sut.ConnectAsync(), Throws.InstanceOf<TimeoutException>());
    }

    /// <summary>Verifies that ConnectAsync can be canceled via its cancellation token.</summary>
    [Test]
    public async Task Connect_can_be_canceled()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddOptions<ClientConnectionOptions>().Configure(
            options => options.ConnectTimeout = TimeSpan.FromMilliseconds(300));

        await using ServiceProvider provider =
            services
                .AddClientServerColocTest(dispatcher: ServiceNotFoundDispatcher.Instance)
                .AddTestDuplexTransportDecorator(
                    serverOperationsOptions: new()
                    {
                        Hold = DuplexTransportOperations.Connect
                    })
                .BuildServiceProvider(validateScopes: true);

        Server server = provider.GetRequiredService<Server>();
        server.Listen();
        ClientConnection sut = provider.GetRequiredService<ClientConnection>();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        Task connectTask = sut.ConnectAsync(cts.Token);

        // Act/Assert
        Assert.That(() => connectTask, Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public async Task Connection_can_reconnect_after_underlying_connection_shutdown()
    {
        // Arrange
        var server = new Server(ServiceNotFoundDispatcher.Instance, new Uri("icerpc://127.0.0.1:0"));
        ServerAddress serverAddress = server.Listen();
        await using var connection = new ClientConnection(serverAddress);
        await connection.ConnectAsync();
        await server.DisposeAsync();
        server = new Server(ServiceNotFoundDispatcher.Instance, serverAddress);
        server.Listen();

        // Act/Assert
        Assert.That(async () => await connection.ConnectAsync(), Throws.Nothing);

        await server.DisposeAsync();
    }

    [Test]
    public async Task Connection_can_reconnect_after_peer_abort()
    {
        // Arrange
        var server = new Server(ServiceNotFoundDispatcher.Instance, new Uri("icerpc://127.0.0.1:0"));
        ServerAddress serverAddress = server.Listen();
        await using var connection = new ClientConnection(serverAddress);
        await connection.ConnectAsync();
        await server.DisposeAsync();
        server = new Server(ServiceNotFoundDispatcher.Instance, serverAddress);
        server.Listen();

        // Act/Assert
        Assert.That(async () => await connection.ConnectAsync(), Throws.Nothing);

        await server.DisposeAsync();
    }

    [Test, TestCaseSource(nameof(Protocols))]
    public async Task Connection_invoke_reconnect_after_underlying_connection_shutdown(Protocol protocol)
    {
        // Arrange
        var server = new Server(ServiceNotFoundDispatcher.Instance, new Uri($"{protocol.Name}://127.0.0.1:0"));
        ServerAddress serverAddress = server.Listen();
        await using var connection = new ClientConnection(serverAddress);
        await connection.ConnectAsync();
        await server.ShutdownAsync();
        await server.DisposeAsync();
        server = new Server(ServiceNotFoundDispatcher.Instance, serverAddress);
        server.Listen();

        {
            // Create a separate scope to ensure the call to DisposeAsync runs after
            // request is disposed by the using directive.
            using var request = new OutgoingRequest(new ServiceAddress(protocol));

            // Act/Assert
            Assert.That(async () => await connection.InvokeAsync(request), Throws.Nothing);
        }

        await server.DisposeAsync();
    }

    [Test, TestCaseSource(nameof(Protocols))]
    public async Task Connection_can_connect_after_connect_failure(Protocol protocol)
    {
        // Arrange
        var colocTransport = new ColocTransport();
        var serverAddress = new ServerAddress(protocol) { Host = "colochost" };
        await using var server = new Server(
            ServiceNotFoundDispatcher.Instance,
            serverAddress,
            duplexServerTransport: colocTransport.ServerTransport,
            multiplexedServerTransport: new SlicServerTransport(colocTransport.ServerTransport));
        await using var connection = new ClientConnection(
            serverAddress,
            duplexClientTransport: colocTransport.ClientTransport,
            multiplexedClientTransport: new SlicClientTransport(colocTransport.ClientTransport));

        // Act/Assert
        IceRpcException? exception = Assert.ThrowsAsync<IceRpcException>(async () => await connection.ConnectAsync());
        server.Listen();
        Assert.That(async () => await connection!.ConnectAsync(), Throws.Nothing);
    }

    /// <summary>Verifies that ClientConnection can dispatch a request sent by the server during the client's first
    /// request.</summary>
    [Test, TestCaseSource(nameof(Protocols))]
    public async Task Connection_can_dispatch_callbacks(Protocol protocol)
    {
        // Arrange
        using var callbackDispatcher = new TestDispatcher();

        var services = new ServiceCollection();
        services.AddOptions<ClientConnectionOptions>().Configure(options => options.Dispatcher = callbackDispatcher);

        await using ServiceProvider provider =
            services.AddClientServerColocTest(
                protocol,
                new InlineDispatcher(
                    async (incomingRequest, cancellationToken) =>
                    {
                        using var outgoingRequest = new OutgoingRequest(new ServiceAddress(protocol));
                        await incomingRequest.ConnectionContext.Invoker.InvokeAsync(outgoingRequest, cancellationToken);
                        return new OutgoingResponse(incomingRequest);
                    }))
            .BuildServiceProvider(validateScopes: true);

        provider.GetRequiredService<Server>().Listen();

        using var request = new OutgoingRequest(new ServiceAddress(protocol));

        // Act
        await provider.GetRequiredService<ClientConnection>().InvokeAsync(request);
        await callbackDispatcher.DispatchStart;
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
        using var dispatcher = new TestDispatcher();
        await using ServiceProvider provider = new ServiceCollection()
            .AddClientServerColocTest(serviceAddress.Protocol, dispatcher, host: "testhost.com")
            .BuildServiceProvider(validateScopes: true);

        Server server = provider.GetRequiredService<Server>();
        ClientConnection connection = provider.GetRequiredService<ClientConnection>();

        server.Listen();
        using var request = new OutgoingRequest(serviceAddress);

        // Assert
        Assert.That(
            async () => await connection.InvokeAsync(request, default),
            Throws.Nothing);
    }

    /// <summary>Verifies that InvokeAsync fails when there is no compatible server address.</summary>
    [TestCase("icerpc://foo.com?transport=tcp", "icerpc://foo.com?transport=coloc")]
    [TestCase("icerpc://foo.com", "icerpc://foo.com?transport=coloc")]
    [TestCase("icerpc://foo.com", "icerpc://bar.com")]
    [TestCase("icerpc://foo.com", "icerpc://foo.com:10000")]
    [TestCase("icerpc://foo.com", "icerpc://foo.com?t=10000")]
    [TestCase("ice://foo.com?t=10000&z", "ice://foo.com:10000/path?t=10000&z")]
    public async Task InvokeAsync_fails_without_a_compatible_server_address(
        ServerAddress serverAddress,
        ServiceAddress serviceAddress)
    {
        // Arrange
        await using var connection = new ClientConnection(serverAddress);
        using var request = new OutgoingRequest(serviceAddress);

        // Assert
        Assert.That(
            async () => await connection.InvokeAsync(request),
            Throws.TypeOf<InvalidOperationException>());
    }

    /// <summary>Verifies that InvokeAsync fails when the protocols don't match.</summary>
    [TestCase("icerpc://foo.com", "ice:/path")]
    [TestCase("ice://foo.com", "icerpc:/path")]
    public async Task InvokeAsync_fails_with_protocol_mismatch(
        ServerAddress serverAddress,
        ServiceAddress serviceAddress)
    {
        // Arrange
        using var dispatcher = new TestDispatcher();
        await using ServiceProvider provider = new ServiceCollection()
            .AddClientServerColocTest(serverAddress.Protocol, dispatcher, host: serverAddress.Host)
            .BuildServiceProvider(validateScopes: true);

        Server server = provider.GetRequiredService<Server>();
        ClientConnection connection = provider.GetRequiredService<ClientConnection>();

        server.Listen();
        using var request = new OutgoingRequest(serviceAddress);

        // Assert
        Assert.That(
            async () => await connection.InvokeAsync(request),
            Throws.TypeOf<InvalidOperationException>());
    }

    /// <summary>Verifies that disposing the client connection aborts an outstanding Connect.</summary>
    [Test]
    public async Task Dispose_aborts_connect()
    {
        // Arrange
        await using ServiceProvider provider =
            new ServiceCollection()
                .AddClientServerColocTest(dispatcher: ServiceNotFoundDispatcher.Instance)
                .AddTestDuplexTransportDecorator(
                    clientOperationsOptions: new()
                    {
                        Hold = DuplexTransportOperations.Connect
                    })
                .BuildServiceProvider(validateScopes: true);

        ClientConnection connection = provider.GetRequiredService<ClientConnection>();
        Task connectTask = connection.ConnectAsync();

        // Act
        await connection.DisposeAsync();

        // Assert
        Assert.That(
            async () => await connectTask,
            Throws.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.OperationAborted));
    }

    [Test]
    public async Task Shutdown_times_out_after_shutdown_timeout()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddOptions<ClientConnectionOptions>().Configure(
            options => options.ShutdownTimeout = TimeSpan.FromMilliseconds(300));

        await using ServiceProvider provider =
            services
                .AddClientServerColocTest(dispatcher: ServiceNotFoundDispatcher.Instance)
                .AddTestDuplexTransportDecorator()
                .BuildServiceProvider(validateScopes: true);

        var serverTransport = provider.GetRequiredService<TestDuplexServerTransportDecorator>();

        Server server = provider.GetRequiredService<Server>();
        ClientConnection connection = provider.GetRequiredService<ClientConnection>();
        server.Listen();
        await connection.ConnectAsync();
        await serverTransport.LastAcceptedConnection.ConnectAsync(default);
        // Hold server reads after the connection is established to prevent shutdown to proceed.
        serverTransport.LastAcceptedConnection.Operations.Hold = DuplexTransportOperations.Read;

        Task shutdownTask = connection.ShutdownAsync();

        // Act/Assert
        Assert.That(async () => await shutdownTask, Throws.InstanceOf<TimeoutException>());
    }

    [Test]
    public async Task Shutdown_can_be_canceled()
    {
        // We use our own decorated server transport
        var colocTransport = new ColocTransport();
        var serverTransport = new TestDuplexServerTransportDecorator(colocTransport.ServerTransport);

        await using ServiceProvider provider =
            new ServiceCollection()
                .AddClientServerColocTest(dispatcher: ServiceNotFoundDispatcher.Instance)
                .AddSingleton(colocTransport.ClientTransport) // overwrite
                .AddSingleton<IDuplexServerTransport>(serverTransport)
                .BuildServiceProvider(validateScopes: true);

        Server server = provider.GetRequiredService<Server>();
        ClientConnection connection = provider.GetRequiredService<ClientConnection>();
        server.Listen();
        await connection.ConnectAsync();

        var serverConnection = serverTransport.LastAcceptedConnection;
        await serverConnection.ConnectAsync(default);

        // Hold server writes after the connection is established to prevent server shutdown to proceed.
        serverConnection.Operations.Hold = DuplexTransportOperations.Write;
        Task writeGoAwayCalledTask = serverConnection.Operations.GetCalledTask(DuplexTransportOperations.Write);

        // Act
        using var cts = new CancellationTokenSource();
        Task shutdownTask = connection.ShutdownAsync(cts.Token);

        // Wait for the server connection shutdown to start writing the GoAway frame before to cancel client shutdown.
        await writeGoAwayCalledTask.ConfigureAwait(false);

        cts.Cancel(); // cancel client shutdown.

        // Assert
        Assert.That(async () => await shutdownTask, Throws.InstanceOf<OperationCanceledException>());
    }
}
