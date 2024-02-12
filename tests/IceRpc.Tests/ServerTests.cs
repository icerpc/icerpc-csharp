// Copyright (c) ZeroC, Inc.

using IceRpc.Internal;
using IceRpc.Tests.Common;
using IceRpc.Transports;
using IceRpc.Transports.Coloc;
using IceRpc.Transports.Slic;
using NUnit.Framework;
using System.Security.Authentication;

namespace IceRpc.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class ServerTests
{
    public static IEnumerable<Exception> AcceptNonFatalException { get; } = new Exception[]
    {
        new IceRpcException(IceRpcError.IceRpcError),
        new AuthenticationException()
    };

    public static IEnumerable<Exception> AcceptFatalException { get; } = new Exception[]
    {
        new IceRpcException(IceRpcError.OperationAborted),
        new Exception(),
    };

    /// <summary>Verifies that calling <see cref="Server.Listen" /> more than once fails with
    /// <see cref="InvalidOperationException" /> exception.</summary>
    [Test]
    public async Task Cannot_call_listen_twice()
    {
        await using var server = new Server(NotFoundDispatcher.Instance, new Uri("icerpc://127.0.0.1:0"));
        server.Listen();

        Assert.Throws<InvalidOperationException>(() => server.Listen());
    }

    /// <summary>Verifies that calling <see cref="Server.Listen" /> on a disposed server fails with
    /// <see cref="ObjectDisposedException" />.</summary>
    [Test]
    public async Task Cannot_call_listen_on_a_disposed_server()
    {
        var server = new Server(NotFoundDispatcher.Instance);
        await server.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() => server.Listen());
    }

    [Test]
    public async Task Connection_refused_after_max_connections_is_reached(
        [Values("icerpc://127.0.0.1:0", "ice://127.0.0.1:0")] Uri serverAddressUri)
    {
        // Arrange
        using var dispatcher = new TestDispatcher();

        await using var server = new Server(
            new ServerOptions
            {
                ConnectionOptions = new ConnectionOptions { Dispatcher = dispatcher },
                MaxConnections = 1,
                ServerAddress = new ServerAddress(serverAddressUri),
            });

        ServerAddress serverAddress = server.Listen();

        await using var connection1 = new ClientConnection(
            new ClientConnectionOptions
            {
                ServerAddress = serverAddress,
            });

        await using var connection2 = new ClientConnection(
            new ClientConnectionOptions
            {
                ServerAddress = serverAddress,
            });

        await connection1.ConnectAsync();

        var exception = Assert.ThrowsAsync<IceRpcException>(() => connection2.ConnectAsync());
        Assert.That(exception!.IceRpcError,
            serverAddress.Protocol == Protocol.Ice ?
                Is.EqualTo(IceRpcError.IceRpcError).Or.EqualTo(IceRpcError.ConnectionAborted) :
                Is.EqualTo(IceRpcError.ServerBusy));
    }

    [Test]
    public async Task Connection_establishment_aborts_if_connection_is_refused_and_close_hangs_or_fails(
        [Values] bool failure)
    {
        // Arrange
        using var dispatcher = new TestDispatcher();

        var colocTransport = new ColocTransport(new ColocTransportOptions { ListenBacklog = 1 });
        var multiplexedServerTransport = new TestMultiplexedServerTransportDecorator(
            new SlicServerTransport(colocTransport.ServerTransport));
        var multiplexedClientTransport = new SlicClientTransport(colocTransport.ClientTransport);

        await using var server = new Server(
            new ServerOptions
            {
                ConnectionOptions = new ConnectionOptions { Dispatcher = dispatcher },
                ConnectTimeout = TimeSpan.FromMilliseconds(300),
                MaxConnections = 1,
                ServerAddress = new ServerAddress(new Uri("icerpc://server")),
            },
            multiplexedServerTransport: multiplexedServerTransport);

        ServerAddress serverAddress = server.Listen();

        await using var connection1 = new ClientConnection(
            new ClientConnectionOptions
            {
                ServerAddress = serverAddress,
            },
            multiplexedClientTransport: multiplexedClientTransport);

        await using var connection2 = new ClientConnection(
            new ClientConnectionOptions
            {
                ServerAddress = serverAddress,
            },
            multiplexedClientTransport: multiplexedClientTransport);

        Assert.That(async () => await connection1.ConnectAsync(), Throws.Nothing);

        // Make sure the connection refusal is aborted if the server side transport CloseAsync call hangs or fails.
        multiplexedServerTransport.ConnectionOperationsOptions = failure ?
            new() { Fail = MultiplexedTransportOperations.Close } :
            new() { Hold = MultiplexedTransportOperations.Close };

        // Act / Assert
        Assert.That(
            () => connection2.ConnectAsync(),
            Throws.TypeOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.ConnectionAborted));
        Assert.That(async () => await connection2.DisposeAsync(), Throws.Nothing);
    }

    [Test]
    public async Task Connection_refused_after_max_pending_connections_is_reached()
    {
        // Arrange
        using var dispatcher = new TestDispatcher();

        var colocTransport = new ColocTransport(new ColocTransportOptions { ListenBacklog = 1 });
        var serverTransport = new SlicServerTransport(new TestDuplexServerTransportDecorator(
            colocTransport.ServerTransport,
            operationsOptions: new() { Hold = DuplexTransportOperations.Connect }));
        var clientTransport = new SlicClientTransport(colocTransport.ClientTransport);

        await using var server = new Server(
           new ServerOptions
           {
               ConnectionOptions = new ConnectionOptions { Dispatcher = dispatcher },
               MaxPendingConnections = 1,
               ServerAddress = new ServerAddress(new Uri("icerpc://server"))
           },
           multiplexedServerTransport: serverTransport);

        ServerAddress serverAddress = server.Listen();

        var clientConnectionOptions = new ClientConnectionOptions()
        {
            ServerAddress = serverAddress
        };

        await using var clientConnection1 = new ClientConnection(
           clientConnectionOptions,
           multiplexedClientTransport: clientTransport);
        await using var clientConnection2 = new ClientConnection(
           clientConnectionOptions,
           multiplexedClientTransport: clientTransport);
        await using var clientConnection3 = new ClientConnection(
           clientConnectionOptions,
           multiplexedClientTransport: clientTransport);

        using CancellationTokenSource connectCts = new();
        Task<TransportConnectionInformation> connectTask1 = clientConnection1.ConnectAsync(connectCts.Token);
        Task<TransportConnectionInformation> connectTask2 = clientConnection2.ConnectAsync(connectCts.Token);
        Task<TransportConnectionInformation> connectTask3 = clientConnection3.ConnectAsync(connectCts.Token);

        // Act
        var completedConnectTask = await Task.WhenAny(connectTask1, connectTask2, connectTask3);

        // Assert
        IceRpcException? exception = Assert.ThrowsAsync<IceRpcException>(async () => await completedConnectTask);
        Assert.That(exception!.IceRpcError, Is.EqualTo(IceRpcError.ConnectionRefused));
        connectCts.Cancel();

        // Cleanup.
        Assert.That(() => Task.WhenAll(connectTask1, connectTask2, connectTask3), Throws.InstanceOf<IceRpcException>());
    }

    [Test]
    public async Task Connection_accepted_when_max_connections_is_reached_then_decremented()
    {
        // Arrange
        using var dispatcher = new TestDispatcher();
        var colocTransport = new ColocTransport();
        var testDuplexServerTransport = new TestDuplexServerTransportDecorator(colocTransport.ServerTransport);
        var serverTransport = new SlicServerTransport(testDuplexServerTransport);
        var clientTransport = new SlicClientTransport(colocTransport.ClientTransport);

        await using var server = new Server(
           new ServerOptions
           {
               ConnectionOptions = new ConnectionOptions { Dispatcher = dispatcher },
               MaxConnections = 1,
               ServerAddress = new ServerAddress(new Uri("icerpc://server"))
           },
           multiplexedServerTransport: serverTransport);

        ServerAddress serverAddress = server.Listen();

        await using var clientConnection1 = new ClientConnection(
            new ClientConnectionOptions { ServerAddress = serverAddress },
            multiplexedClientTransport: clientTransport);

        await using var clientConnection2 = new ClientConnection(
            new ClientConnectionOptions { ServerAddress = serverAddress },
            multiplexedClientTransport: clientTransport);

        await using var clientConnection3 = new ClientConnection(
            new ClientConnectionOptions { ServerAddress = serverAddress },
            multiplexedClientTransport: clientTransport);

        await clientConnection1.ConnectAsync();
        var testConnection = testDuplexServerTransport.LastAcceptedConnection;
        Task disposeCalledTask = testConnection.Operations.GetCalledTask(DuplexTransportOperations.Dispose);

        // Act/Assert
        Assert.That(
            () => clientConnection2.ConnectAsync(),
            Throws.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.ServerBusy));

        // Shutdown the first connection. This should allow the second connection to be accepted once it's been disposed
        // thus removed from the server's connection list.
        Assert.That(() => clientConnection1.ShutdownAsync(), Throws.Nothing);
        await disposeCalledTask;
        // Add a small delay to ensure the sever decremented the connection count after disposing the connection.
        await Task.Delay(TimeSpan.FromMilliseconds(50));
        Assert.That(() => clientConnection3.ConnectAsync(), Throws.Nothing);
    }

    [Test]
    public async Task Dispose_waits_for_background_connection_dispose()
    {
        // Arrange
        var dispatcher = new InlineDispatcher((request, cancellationToken) => new(new OutgoingResponse(request)));

        var colocTransport = new ColocTransport();
        var multiplexedServerTransport = new TestMultiplexedServerTransportDecorator(
            new SlicServerTransport(colocTransport.ServerTransport));
        var multiplexedClientTransport = new SlicClientTransport(colocTransport.ClientTransport);

        await using var server = new Server(
            new ServerOptions
            {
                ConnectionOptions = new ConnectionOptions { Dispatcher = dispatcher },
                ServerAddress = new ServerAddress(new Uri("icerpc://foo"))
            },
            multiplexedServerTransport: multiplexedServerTransport);

        await using var clientConnection = new ClientConnection(
            server.Listen(),
            multiplexedClientTransport: multiplexedClientTransport);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        IncomingResponse response = await clientConnection.InvokeAsync(request);
        response.Payload.Complete();

        TestMultiplexedConnectionDecorator serverConnection = multiplexedServerTransport.LastAcceptedConnection!;
        serverConnection.Operations.Hold = MultiplexedTransportOperations.Dispose;
        Task disposeCalledTask = serverConnection.Operations.GetCalledTask(MultiplexedTransportOperations.Dispose);

        // Shutdown the client connection to trigger the background server connection disposal.
        await clientConnection.ShutdownAsync();

        // Act
        ValueTask disposeTask = server.DisposeAsync();

        // Assert
        await disposeCalledTask;
        using var cts = new CancellationTokenSource(100);
        Assert.That(() => disposeTask.AsTask().WaitAsync(cts.Token), Throws.InstanceOf<OperationCanceledException>());
        serverConnection.Operations.Hold = MultiplexedTransportOperations.None; // Release dispose
        await disposeTask;
    }

    [TestCaseSource(nameof(AcceptNonFatalException))]
    public async Task Server_continues_accepting_connections_on_non_fatal_accept_exception(Exception exception)
    {
        // Arrange

        var colocTransport = new ColocTransport();
        var multiplexedServerTransport = new TestMultiplexedServerTransportDecorator(
            new SlicServerTransport(colocTransport.ServerTransport),
            operationsOptions: new() { FailureException = exception });
        var multiplexedClientTransport = new SlicClientTransport(colocTransport.ClientTransport);

        await using var server = new Server(
            dispatcher: NotFoundDispatcher.Instance,
            serverAddress: new ServerAddress(new Uri("icerpc://foo")),
            multiplexedServerTransport: multiplexedServerTransport);

        // Wait for accept to be called to configure the failure in order to trigger the failure after the connection
        // is accepted by the decorated transport.
        Task acceptCalledTask = multiplexedServerTransport.ListenerOperations.GetCalledTask(
            MultiplexedTransportOperations.Accept);
        var serverAddress = server.Listen();
        await acceptCalledTask;
        multiplexedServerTransport.ListenerOperations.Fail = MultiplexedTransportOperations.Accept;

        await using var clientConnection = new ClientConnection(
            serverAddress,
            multiplexedClientTransport: multiplexedClientTransport);

        // Act/Assert

        // Since the test transport accepts the connection and immediately disposes it before throwing the failure
        // exception, the connection establishment fail with ConnectionAborted is expected here.
        Assert.That(
            () => clientConnection.ConnectAsync(),
            Throws.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.ConnectionAborted));

        multiplexedServerTransport.ListenerOperations.Fail = MultiplexedTransportOperations.None;

        Assert.That(() => clientConnection.ConnectAsync(), Throws.Nothing);
    }

    [TestCaseSource(nameof(AcceptFatalException))]
    public async Task Server_stops_accepting_connections_on_fatal_accept_exception(Exception exception)
    {
        // Arrange

        var colocTransport = new ColocTransport();
        var multiplexedServerTransport = new TestMultiplexedServerTransportDecorator(
            new SlicServerTransport(colocTransport.ServerTransport),
            operationsOptions: new() { FailureException = exception });
        var multiplexedClientTransport = new SlicClientTransport(colocTransport.ClientTransport);

        await using var server = new Server(
            dispatcher: NotFoundDispatcher.Instance,
            serverAddress: new ServerAddress(new Uri("icerpc://foo")),
            multiplexedServerTransport: multiplexedServerTransport);

        // Wait for accept to be called to configure the failure in order to trigger the failure after the connection
        // is accepted by the decorated transport.
        Task acceptCalledTask = multiplexedServerTransport.ListenerOperations.GetCalledTask(
            MultiplexedTransportOperations.Accept);
        var serverAddress = server.Listen();
        await acceptCalledTask;
        multiplexedServerTransport.ListenerOperations.Fail = MultiplexedTransportOperations.Accept;

        await using var clientConnection = new ClientConnection(
            serverAddress,
            multiplexedClientTransport: multiplexedClientTransport);

        // Act/Assert

        // Since the test transport accepts the connection and immediately disposes it before throwing the failure
        // exception, the connection establishment failing with ConnectionAborted is expected here.

        Assert.That(
            () => clientConnection.ConnectAsync(),
            Throws.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.ConnectionAborted));

        // The server listener is disposed on an accept fatal failure so the next connection establishment results in a
        // connection refused error.
        Assert.That(
            () => clientConnection.ConnectAsync(),
            Throws.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.ConnectionRefused));
    }
}
