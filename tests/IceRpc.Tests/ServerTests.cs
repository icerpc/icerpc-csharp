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
    public async Task Connection_refused_after_max_connections_is_reached()
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
    public async Task Connection_refused_after_max_pending_connections_is_reached()
    {
        // Arrange
        var dispatcher = new InlineDispatcher((request, cancellationToken) => new(new OutgoingResponse(request)));

        var colocTransport = new ColocTransport(new ColocTransportOptions { ListenBacklog = 1 });
        var serverTransport = new SlicServerTransport(new HoldServerTransport(colocTransport.ServerTransport));
        var clientTransport = new SlicClientTransport(colocTransport.ClientTransport);

        await using var server = new Server(
           new ServerOptions
           {
               ConnectionOptions = new ConnectionOptions { Dispatcher = dispatcher },
               MaxPendingConnections = 1,
               ServerAddress = new ServerAddress(new Uri("icerpc://server"))
           },
           multiplexedServerTransport: serverTransport);

        server.Listen();

        var clientConnectionOptions = new ClientConnectionOptions()
        {
            ServerAddress = server.ServerAddress
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

        Task<TransportConnectionInformation> connectTask1 = clientConnection1.ConnectAsync();
        Task<TransportConnectionInformation> connectTask2 = clientConnection2.ConnectAsync();
        Task<TransportConnectionInformation> connectTask3 = clientConnection3.ConnectAsync();

        // Act
        var completedConnectTask = await Task.WhenAny(connectTask1, connectTask2, connectTask3);

        // Assert

        // TODO: if any of the Assert fails, we need to dispose the server first, otherwise the test hangs and hides the
        // actual Assert failure.
        try
        {
            IceRpcException? exception = Assert.ThrowsAsync<IceRpcException>(async () => await completedConnectTask);
            Assert.That(exception!.IceRpcError, Is.EqualTo(IceRpcError.ConnectionRefused));
        }
        finally
        {
            await server.DisposeAsync();
        }
    }

    [Test]
    [Ignore("see issue #2295")]
    public async Task Connection_accepted_when_max_connections_is_reached_then_decremented()
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
        DelayDisposeMultiplexedConnection serverConnection1 = serverListener.LastConnection!;

        // Act/Assert
        Assert.That(
            () => clientConnection2.ConnectAsync(),
            Throws.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.ServerBusy));

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

    private sealed class HoldServerTransport : IDuplexServerTransport
    {
        public string Name => _serverTransport.Name;

        private readonly IDuplexServerTransport _serverTransport;

        public IListener<IDuplexConnection> Listen(
            ServerAddress serverAddress,
            DuplexConnectionOptions options,
            SslServerAuthenticationOptions? serverAuthenticationOptions) =>
            new HoldListener(_serverTransport.Listen(serverAddress, options, serverAuthenticationOptions));

        internal HoldServerTransport(IDuplexServerTransport serverTransport) => _serverTransport = serverTransport;
    }

    private sealed class HoldListener : IListener<IDuplexConnection>
    {
        public ServerAddress ServerAddress => _listener.ServerAddress;

        private bool _firstConnect = true;
        private readonly IListener<IDuplexConnection> _listener;

        internal HoldListener(IListener<IDuplexConnection> listener) => _listener = listener;

        public async Task<(IDuplexConnection Connection, EndPoint RemoteNetworkAddress)> AcceptAsync(
            CancellationToken cancellationToken)
        {
            (IDuplexConnection connection, EndPoint remoteNetworkAddress) = await _listener.AcceptAsync(
                cancellationToken);
            if (_firstConnect)
            {
                _firstConnect = false;
#pragma warning disable CA2000
                connection = new HoldServerConnection(connection);
#pragma warning restore CA2000
            }
            return (connection, remoteNetworkAddress);
        }

        public ValueTask DisposeAsync() => _listener.DisposeAsync();
    }

    private sealed class HoldServerConnection : IDuplexConnection
    {
        public ServerAddress ServerAddress => _connection.ServerAddress;

        private readonly IDuplexConnection _connection;

        public async Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(-1, cancellationToken);
            return new(new IPEndPoint(0, 0), new IPEndPoint(0, 0), remoteCertificate: null);
        }

        public void Dispose() => _connection.Dispose();

        public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task ShutdownAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask WriteAsync(IReadOnlyList<ReadOnlyMemory<byte>> buffers, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        internal HoldServerConnection(IDuplexConnection connection) => _connection = connection;
    }

    private sealed class DelayDisposeMultiplexServerTransport : IMultiplexedServerTransport
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

    private sealed class DelayDisposeConnectionListener : IListener<IMultiplexedConnection>
    {
        public ServerAddress ServerAddress => _listener.ServerAddress;

        // Gets the last connection created by this listener
        public DelayDisposeMultiplexedConnection? LastConnection { get; private set; }

        // Sets whether to delay dispose of the next connection created by this listener
        public bool DelayDisposeNewConnections { private get; set; } = true;

        private readonly IListener<IMultiplexedConnection> _listener;

        public DelayDisposeConnectionListener(IListener<IMultiplexedConnection> listener) => _listener = listener;

        public async Task<(IMultiplexedConnection Connection, EndPoint RemoteNetworkAddress)>
        AcceptAsync(CancellationToken cancellationToken)
        {
            (IMultiplexedConnection connection, EndPoint endpoint) = await _listener.AcceptAsync(cancellationToken);
            LastConnection = new DelayDisposeMultiplexedConnection(connection, DelayDisposeNewConnections);
            return (LastConnection, endpoint);
        }

        public ValueTask DisposeAsync() => _listener.DisposeAsync();
    }

    private sealed class DelayDisposeMultiplexedConnection : IMultiplexedConnection
    {
        public ServerAddress ServerAddress => _connection.ServerAddress;

        private readonly IMultiplexedConnection _connection;

        private readonly bool _delayDispose;

        private readonly SemaphoreSlim _waitDisposeSemaphore = new(0);
        private readonly SemaphoreSlim _delayDisposeSemaphore = new(0);

        public DelayDisposeMultiplexedConnection(IMultiplexedConnection connection, bool delayDispose)
        {
            _connection = connection;
            _delayDispose = delayDispose;
        }

        public ValueTask<IMultiplexedStream> AcceptStreamAsync(CancellationToken cancellationToken) =>
            _connection.AcceptStreamAsync(cancellationToken);

        public Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken) =>
            _connection.ConnectAsync(cancellationToken);

        public Task CloseAsync(MultiplexedConnectionCloseError closeError, CancellationToken cancellationToken) =>
            _connection.CloseAsync(closeError, cancellationToken);

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
            _waitDisposeSemaphore.Dispose();
            _delayDisposeSemaphore.Dispose();
        }

        public Task WaitForDisposeStart() => _waitDisposeSemaphore.WaitAsync(CancellationToken.None);

        public void ReleaseDispose() => _delayDisposeSemaphore.Release();
    }
}
