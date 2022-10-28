// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports;
using NUnit.Framework;
using System.Net;
using System.Net.Security;

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

    /// <summary>Verifies that <see cref="Server.ShutdownComplete" /> task is completed after
    /// <see cref="Server.ShutdownAsync(CancellationToken)" /> completed.</summary>
    [Test]
    public async Task The_shutdown_complete_task_is_completed_after_shutdown()
    {
        await using var server = new Server(ServiceNotFoundDispatcher.Instance);

        await server.ShutdownAsync();

        Assert.That(server.ShutdownComplete.IsCompleted, Is.True);
    }

    /// <summary>Verifies that Server.ServerAddress.Transport property is set.</summary>
    [Test]
    public async Task Server_server_address_transport_property_is_set([Values("ice", "icerpc")] string protocol)
    {
        // Arrange/Act
        await using var server = new Server(
            ServiceNotFoundDispatcher.Instance,
            new ServerAddress(Protocol.Parse(protocol)));

        // Assert
        Assert.That(server.ServerAddress.Transport, Is.Not.Null);
    }

    [Test]
    public async Task Connection_refused_after_max_is_reached()
    {
        // Arrange
        var dispatcher = new InlineDispatcher((request, cancellationToken) => new(new OutgoingResponse(request)));
        // var colocTransport = new ColocTransport();
        await using var server = new Server(
            new ServerOptions
            {
                ConnectionOptions = new ConnectionOptions { Dispatcher = dispatcher },
                MaxConnections = 1,
                ServerAddress = new ServerAddress(new Uri("icerpc://127.0.0.1:0")),
            });

        server.Listen();

        await using var connection1 = new ClientConnection(
            new ClientConnectionOptions
            {
                ServerAddress = server.ServerAddress,
            });

        await using var connection2 = new ClientConnection(
            new ClientConnectionOptions
            {
                ServerAddress = server.ServerAddress,
            });

        await connection1.ConnectAsync();

        Assert.That(() => connection2.ConnectAsync(), Throws.Exception);
    }

    [Test]
    public async Task Connection_accepted_when_max_counter_is_reached_then_decremented()
    {
        // Arrange
        var dispatcher = new InlineDispatcher((request, cancellationToken) => new(new OutgoingResponse(request)));
        var colocTransport = new ColocTransport();

        var serverTransport = new DelayDisposeMultiplexServerTransport(
            new SlicServerTransport(colocTransport.ServerTransport));

        await using var server = new Server(
           new ServerOptions
           {
               ConnectionOptions = new ConnectionOptions { Dispatcher = dispatcher },
               MaxConnections = 1,
               ServerAddress = new ServerAddress(new Uri("icerpc://server"))
           },
           multiplexedServerTransport: serverTransport);

        server.Listen();

        // The listener is now available
        DelayDisposeConnectionListener serverListener = serverTransport.Listener!;

        await using var clientConnection1 = new ClientConnection(
            new ClientConnectionOptions { ServerAddress = server.ServerAddress },
            multiplexedClientTransport: new SlicClientTransport(colocTransport.ClientTransport));

        await using var clientConnection2 = new ClientConnection(
            new ClientConnectionOptions { ServerAddress = server.ServerAddress },
            multiplexedClientTransport: new SlicClientTransport(colocTransport.ClientTransport));

        await using var clientConnection3 = new ClientConnection(
            new ClientConnectionOptions { ServerAddress = server.ServerAddress },
            multiplexedClientTransport: new SlicClientTransport(colocTransport.ClientTransport));

        // No need to delay the disposal of any connections (the default behavior)
        serverListener.DelayDisposeNewConnections = false;

        await clientConnection1.ConnectAsync();
        DelayDisposeMultiplexedConneciton serverConnection1 = serverListener.LastConnection!;

        // Act/Assert
        try
        {
            await clientConnection2.ConnectAsync();
        }
        catch (ConnectionException ex) when (ex.ErrorCode == ConnectionErrorCode.ConnectRefused)
        {
            // The connection is refused because the max connection count is reached
        }
        catch (TransportException ex) when (ex.ErrorCode == TransportErrorCode.ConnectionReset)
        {
            // The transport was closed by the server before the refused message was received
        }

        // Shutdown the first connection. This should allow the second connection to be accepted once it's been disposed
        // thus removed from the server's connection list.
        await clientConnection1.ShutdownAsync();
        await serverConnection1.WaitForDisposeStart();
        await clientConnection3.ConnectAsync();
    }

    [Test]
    public async Task Dispose_waits_for_background_connection_dispose()
    {
        // Arrange
        using var dispatchSemaphore = new SemaphoreSlim(0);
        using var connectSemaphore = new SemaphoreSlim(0);
        IConnectionContext? serverConnectionContext = null;
        var dispatcher = new InlineDispatcher(async (request, cancellationToken) =>
        {
            serverConnectionContext = request.ConnectionContext;
            connectSemaphore.Release();
            await dispatchSemaphore.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            return new OutgoingResponse(request);
        });
        await using var server = new Server(
            new ServerOptions
            {
                ConnectionOptions = new ConnectionOptions
                {
                    Dispatcher = dispatcher,
                    ShutdownTimeout = TimeSpan.FromMilliseconds(500),
                },
                ServerAddress = new ServerAddress(new Uri("icerpc://127.0.0.1:0")),
            });

        server.Listen();

        await using var clientConnection = new ClientConnection(
           new ClientConnectionOptions
           {
               ShutdownTimeout = TimeSpan.FromMilliseconds(500),
               ServerAddress = server.ServerAddress,
           });

        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        Task<IncomingResponse> invokeTask = clientConnection.InvokeAsync(request);

        // Wait for invocation to be dispatched. Then shutdown the client and server connections.
        // Since the dispatch is blocking we wait for shutdown to timeout (We use a 500ms timeout).
        await connectSemaphore.WaitAsync(CancellationToken.None).ConfigureAwait(false);

        try
        {
            await clientConnection.ShutdownAsync(CancellationToken.None).ConfigureAwait(false);
            await serverConnectionContext!.ShutdownComplete.ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
        }

        // Act

        // Dispose the server. This will wait for the background connection dispose to complete.
        ValueTask disposeTask = server.DisposeAsync();

        // Assert
        await Task.Delay(TimeSpan.FromSeconds(1));
        Assert.That(disposeTask.IsCompleted, Is.False);
        // Release the dispatch semaphore, allowing the background connection dispose to complete.
        dispatchSemaphore.Release();
        await disposeTask;
    }

    [Test]
    public async Task Dispose_waits_for_refused_connection_disposal()
    {
        // Arrange
        var dispatcher = new InlineDispatcher((request, cancellationToken) => new(new OutgoingResponse(request)));
        var colocTransport = new ColocTransport();

        var serverTransport = new DelayDisposeMultiplexServerTransport(
            new SlicServerTransport(colocTransport.ServerTransport));

        await using var server = new Server(
           new ServerOptions
           {
               ConnectionOptions = new ConnectionOptions { Dispatcher = dispatcher },
               MaxConnections = 1,
               ServerAddress = new ServerAddress(new Uri("icerpc://server"))
           },
           multiplexedServerTransport: serverTransport);

        server.Listen();

        // The listener is now available
        DelayDisposeConnectionListener serverListener = serverTransport.Listener!;

        await using var clientConnection1 = new ClientConnection(
           new ClientConnectionOptions() { ServerAddress = new ServerAddress(new Uri("icerpc://server")) },
           multiplexedClientTransport: new SlicClientTransport(colocTransport.ClientTransport));

        await using var clientConnection2 = new ClientConnection(
           new ClientConnectionOptions() { ServerAddress = new ServerAddress(new Uri("icerpc://server")) },
           multiplexedClientTransport: new SlicClientTransport(colocTransport.ClientTransport));

        // Don't delay dispose of the first server connection
        serverListener.DelayDisposeNewConnections = false;
        await clientConnection1.ConnectAsync();

        DelayDisposeMultiplexedConneciton serverConnection1 = serverListener.LastConnection!;
        try
        {
            // Delay dispose of the second server connection
            serverListener.DelayDisposeNewConnections = true;
            await clientConnection2.ConnectAsync();
            // This should always throw
        }
        catch (ConnectionException)
        {
            // Connection refused
        }

        DelayDisposeMultiplexedConneciton serverConnection2 = serverListener.LastConnection!;

        // Wait for the connection to began disposal.
        await serverConnection2.WaitForDisposeStart();

        // Shutdown the first client connection and wait for the corresponding server connection to be disposed.
        await clientConnection1.ShutdownAsync();
        await serverConnection1.WaitForDisposeStart();

        // Act

        // Dispose the server. This will wait for the background connection dispose to complete.
        ValueTask disposeTask = server.DisposeAsync();

        // Assert
        await Task.Delay(TimeSpan.FromSeconds(1));
        Assert.That(disposeTask.IsCompleted, Is.False);
        serverConnection2.ReleaseDispose();
        await disposeTask;
    }

    private class DelayDisposeMultiplexServerTransport : IMultiplexedServerTransport
    {
        public string Name => _serverTransport.Name;

        public DelayDisposeConnectionListener? Listener { get; set; }

        private readonly IMultiplexedServerTransport _serverTransport;

        public DelayDisposeMultiplexServerTransport(IMultiplexedServerTransport serverTransport)
            => _serverTransport = serverTransport;

        public IListener<IMultiplexedConnection> Listen(
        ServerAddress serverAddress,
        MultiplexedConnectionOptions options,
        SslServerAuthenticationOptions? serverAuthenticationOptions)
        {
            Listener = new DelayDisposeConnectionListener(
                _serverTransport.Listen(serverAddress, options, serverAuthenticationOptions));
            return Listener;
        }
    }

    private class DelayDisposeConnectionListener : IListener<IMultiplexedConnection>
    {
        public ServerAddress ServerAddress => _listener.ServerAddress;

        // Gets the last connection created by this listener
        public DelayDisposeMultiplexedConneciton? LastConnection { get; private set; }

        // Sets whether to delay dispose of the next connection created by this listener
        public bool DelayDisposeNewConnections { private get; set; } = true;

        private readonly IListener<IMultiplexedConnection> _listener;

        public DelayDisposeConnectionListener(IListener<IMultiplexedConnection> listener) => _listener = listener;

        public async Task<(IMultiplexedConnection Connection, EndPoint RemoteNetworkAddress)>
        AcceptAsync(CancellationToken cancellationToken)
        {
            (IMultiplexedConnection connection, EndPoint endpoint) = await _listener.AcceptAsync(cancellationToken);
            LastConnection = new DelayDisposeMultiplexedConneciton(connection, DelayDisposeNewConnections);
            return (LastConnection, endpoint);
        }

        public ValueTask DisposeAsync() => _listener.DisposeAsync();
    }

    private class DelayDisposeMultiplexedConneciton : IMultiplexedConnection
    {
        public ServerAddress ServerAddress => _connection.ServerAddress;

        private readonly IMultiplexedConnection _connection;

        private readonly bool _delayDispose;

        private readonly SemaphoreSlim _waitDisposeSemaphore = new(0);
        private readonly SemaphoreSlim _delayDisposeSemaphore = new(0);

        public DelayDisposeMultiplexedConneciton(IMultiplexedConnection connection, bool delayDispose)
        {
            _connection = connection;
            _delayDispose = delayDispose;
        }

        public ValueTask<IMultiplexedStream> AcceptStreamAsync(CancellationToken cancellationToken) =>
            _connection.AcceptStreamAsync(cancellationToken);

        public Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken) =>
            _connection.ConnectAsync(cancellationToken);

        public Task CloseAsync(ulong applicationErrorCode, CancellationToken cancellationToken) =>
            _connection.CloseAsync(applicationErrorCode, cancellationToken);

        public ValueTask<IMultiplexedStream> CreateStreamAsync(bool bidirectional, CancellationToken cancellationToken)
            => _connection.CreateStreamAsync(bidirectional, cancellationToken);

        public async ValueTask DisposeAsync()
        {
            _waitDisposeSemaphore.Release();

            if (_delayDispose)
            {
                await _delayDisposeSemaphore.WaitAsync(CancellationToken.None).ConfigureAwait(false); ;
            }
            await _connection.DisposeAsync();
        }

        public Task WaitForDisposeStart() => _waitDisposeSemaphore.WaitAsync(CancellationToken.None);

        public void ReleaseDispose() => _delayDisposeSemaphore.Release();
    }
}
