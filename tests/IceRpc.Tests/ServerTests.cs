// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Tests.Common;
using IceRpc.Transports;
using NUnit.Framework;

namespace IceRpc.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class ServerTests
{
    /// <summary>Verifies that calling <see cref="Server.Listen" /> more than once fails with
    /// <see cref="InvalidOperationException" /> exception.</summary>
    [Test]
    public async Task Cannot_call_listen_twice()
    {
        await using var server = new Server(ServiceNotFoundDispatcher.Instance);
        server.Listen();

        Assert.Throws<InvalidOperationException>(() => server.Listen());
    }

    /// <summary>Verifies that calling <see cref="Server.Listen" /> on a disposed server fails with
    /// <see cref="ObjectDisposedException" />.</summary>
    [Test]
    public async Task Cannot_call_listen_on_a_disposed_server()
    {
        var server = new Server(ServiceNotFoundDispatcher.Instance);
        await server.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() => server.Listen());
    }

    [Test]
    public async Task Connection_refused_after_max_connections_is_reached(
        [Values("icerpc://127.0.0.1:0", "ice://127.0.0.1:0")] Uri serverAddressUri)
    {
        // Arrange
        var dispatcher = new TestDispatcher(holdDispatchCount: 0);

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
        [Values(true, false)] bool failure)
    {
        // Arrange
        var dispatcher = new TestDispatcher(holdDispatchCount: 0);

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
        if (failure)
        {
            multiplexedServerTransport.FailOperation = MultiplexedTransportOperation.Close;
        }
        else
        {
            multiplexedServerTransport.HoldOperation = MultiplexedTransportOperation.Close;
        }

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
        var dispatcher = new TestDispatcher(holdDispatchCount: 0);

        var colocTransport = new ColocTransport(new ColocTransportOptions { ListenBacklog = 1 });
        var serverTransport = new SlicServerTransport(new TestDuplexServerTransportDecorator(
            colocTransport.ServerTransport,
            holdOperation: DuplexTransportOperation.Connect));
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

        // TODO: if any of the Assert fails, we need to dispose the server first, otherwise the test hangs and hides the
        // actual Assert failure.
        try
        {
            IceRpcException? exception = Assert.ThrowsAsync<IceRpcException>(async () => await completedConnectTask);
            Assert.That(exception!.IceRpcError, Is.EqualTo(IceRpcError.ConnectionRefused));
            connectCts.Cancel();
        }
        finally
        {
            await server.DisposeAsync();
        }

        // Cleanup.
        Assert.That(() => Task.WhenAll(connectTask1, connectTask2, connectTask3), Throws.InstanceOf<IceRpcException>());
    }

    [Test]
    public async Task Connection_accepted_when_max_connections_is_reached_then_decremented()
    {
        // Arrange
        var dispatcher = new TestDispatcher(holdDispatchCount: 0);
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

        // Act/Assert
        Assert.That(
            () => clientConnection2.ConnectAsync(),
            Throws.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.ServerBusy));

        // Shutdown the first connection. This should allow the second connection to be accepted once it's been disposed
        // thus removed from the server's connection list.
        Assert.That(() => clientConnection1.ShutdownAsync(), Throws.Nothing);
        await testConnection.DisposeCalled;
        Assert.That(() => clientConnection3.ConnectAsync(), Throws.Nothing);
    }

    [Test]
    public async Task Dispose_waits_for_background_connection_dispose()
    {
        // Arrange
        using var dispatchHoldSemaphore = new SemaphoreSlim(0);
        using var dispatchStartedSemaphore = new SemaphoreSlim(0);
        IConnectionContext? serverConnectionContext = null;
        var dispatcher = new InlineDispatcher(async (request, cancellationToken) =>
        {
            serverConnectionContext = request.ConnectionContext;
            dispatchStartedSemaphore.Release();
            await dispatchHoldSemaphore.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            return new OutgoingResponse(request);
        });
        await using var server = new Server(
            new ServerOptions
            {
                ConnectionOptions = new ConnectionOptions { Dispatcher = dispatcher },
                ServerAddress = new ServerAddress(new Uri("icerpc://127.0.0.1:0")),
                ShutdownTimeout = TimeSpan.FromMilliseconds(10),
            });

        ServerAddress serverAddress = server.Listen();

        await using var clientConnection = new ClientConnection(serverAddress);

        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        Task<IncomingResponse> invokeTask = clientConnection.InvokeAsync(request);

        // Wait for invocation to be dispatched.
        await dispatchStartedSemaphore.WaitAsync(CancellationToken.None).ConfigureAwait(false);

        // Act
        Exception? exception = null;
        try
        {
            // Shutdown the client connection.
            await clientConnection.ShutdownAsync().ConfigureAwait(false);
        }
        catch (IceRpcException ex)
        {
            // Expected. The server shutdown timed out and aborted the connection.
            exception = ex;
        }

        // Ensure the server connection shutdown timed out. At this point, Server should have called DisposeAsync on the
        // connection (which completes the Closed task).
        await serverConnectionContext!.Closed.ConfigureAwait(false);

        // Dispose the server. This will wait for the background connection dispose to complete.
        ValueTask disposeTask = server.DisposeAsync();
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        // Assert
        Assert.That(disposeTask.IsCompleted, Is.False);
        Assert.That(exception, Is.Not.Null.And.With.Property("IceRpcError").EqualTo(IceRpcError.ConnectionAborted));

        // Release the dispatch semaphore, allowing the background connection dispose to complete.
        dispatchHoldSemaphore.Release();

        await disposeTask;

        Assert.That(
            async () => await invokeTask,
            Throws.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.ConnectionAborted));
    }
}
