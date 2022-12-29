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

        ServerAddress serverAddress = server.Listen();

        // The listener is now available
        DelayDisposeConnectionListener serverListener = serverTransport.Listener!;

        await using var clientConnection1 = new ClientConnection(
            new ClientConnectionOptions { ServerAddress = serverAddress },
            multiplexedClientTransport: new SlicClientTransport(colocTransport.ClientTransport));

        await using var clientConnection2 = new ClientConnection(
            new ClientConnectionOptions { ServerAddress = serverAddress },
            multiplexedClientTransport: new SlicClientTransport(colocTransport.ClientTransport));

        await using var clientConnection3 = new ClientConnection(
            new ClientConnectionOptions { ServerAddress = serverAddress },
            multiplexedClientTransport: new SlicClientTransport(colocTransport.ClientTransport));

        await clientConnection1.ConnectAsync();
        DelayDisposeMultiplexedConnection serverConnection1 = serverListener.FirstConnection!;

        // Act/Assert
        Assert.That(
            () => clientConnection2.ConnectAsync(),
            Throws.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.ServerBusy));

        // Shutdown the first connection. This should allow the second connection to be accepted once it's been disposed
        // thus removed from the server's connection list.
        Assert.That(() => clientConnection1.ShutdownAsync(), Throws.Nothing);
        await serverConnection1.WaitForDispose;
        Assert.That(() => clientConnection3.ConnectAsync(), Throws.Nothing);
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

        ServerAddress serverAddress = server.Listen();

        await using var clientConnection = new ClientConnection(
           new ClientConnectionOptions
           {
               ShutdownTimeout = TimeSpan.FromMilliseconds(500),
               ServerAddress = serverAddress,
           });

        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        Task<IncomingResponse> invokeTask = clientConnection.InvokeAsync(request);

        // Wait for invocation to be dispatched. Then shutdown the client and server connections.
        // Since the dispatch is blocking we wait for shutdown to timeout (We use a 500ms timeout).
        await connectSemaphore.WaitAsync(CancellationToken.None).ConfigureAwait(false);

        try
        {
            await clientConnection.ShutdownAsync().ConfigureAwait(false);
            await serverConnectionContext!.Closed.ConfigureAwait(false);
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

        // Prevent unobserved task exception.
        Assert.That(async () => await invokeTask, Throws.InstanceOf<IceRpcException>());
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
        // Gets the first connection created by this listener
        public DelayDisposeMultiplexedConnection? FirstConnection { get; private set; }

        public ServerAddress ServerAddress => _listener.ServerAddress;

        private readonly IListener<IMultiplexedConnection> _listener;

        public DelayDisposeConnectionListener(IListener<IMultiplexedConnection> listener) => _listener = listener;

        public async Task<(IMultiplexedConnection Connection, EndPoint RemoteNetworkAddress)>
        AcceptAsync(CancellationToken cancellationToken)
        {
            (IMultiplexedConnection connection, EndPoint endpoint) = await _listener.AcceptAsync(cancellationToken);
            if (FirstConnection is null)
            {
                FirstConnection = new DelayDisposeMultiplexedConnection(connection);
                return (FirstConnection, endpoint);
            }
            else
            {
                return (connection, endpoint);
            }
        }

        public ValueTask DisposeAsync() => _listener.DisposeAsync();
    }

    private sealed class DelayDisposeMultiplexedConnection : IMultiplexedConnection
    {
        public ServerAddress ServerAddress => _decoratee.ServerAddress;
        public Task WaitForDispose => _waitForDisposeTcs.Task;

        private readonly IMultiplexedConnection _decoratee;

        private readonly TaskCompletionSource _waitForDisposeTcs = new();

        public DelayDisposeMultiplexedConnection(IMultiplexedConnection decoratee) =>
            _decoratee = decoratee;

        public ValueTask<IMultiplexedStream> AcceptStreamAsync(CancellationToken cancellationToken) =>
            _decoratee.AcceptStreamAsync(cancellationToken);

        public Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken) =>
            _decoratee.ConnectAsync(cancellationToken);

        public Task CloseAsync(MultiplexedConnectionCloseError closeError, CancellationToken cancellationToken) =>
            _decoratee.CloseAsync(closeError, cancellationToken);

        public ValueTask<IMultiplexedStream> CreateStreamAsync(
            bool bidirectional,
            CancellationToken cancellationToken) =>
            _decoratee.CreateStreamAsync(bidirectional, cancellationToken);

        public ValueTask DisposeAsync()
        {
            // When Server calls DisposeAsync, the connection has been removed from the Server's connection set.
            _waitForDisposeTcs.SetResult();
            return _decoratee.DisposeAsync();
        }
    }
}
