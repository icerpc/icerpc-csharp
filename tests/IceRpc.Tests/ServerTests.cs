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

        // Act/Assert
        ConnectionException? exception = Assert.ThrowsAsync<ConnectionException>(() => connection2.ConnectAsync());
        Assert.That(exception!.ErrorCode, Is.EqualTo(ConnectionErrorCode.ConnectRefused));
        await connection1.ShutdownAsync();

        // Artificial delay to ensure the server has time to dispose and cleanup the connection.
        // This is not ideal but it's the best we can do for now.
        int retry = 5;
        while (retry-- > 0)
        {
            await Task.Delay(TimeSpan.FromSeconds(1));
            try
            {
                await connection2.ConnectAsync();
                break;
            }
            catch (ConnectionException)
            {
            }
        }
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

        Task<IncomingResponse> invokeTask =
            clientConnection.InvokeAsync(new OutgoingRequest(new ServiceAddress(Protocol.IceRpc)));

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

        using var refusedSemaphore = new SemaphoreSlim(0);
        using var disposeSemaphore = new SemaphoreSlim(0);

        var serverTransport = new RefusedMultiplexServerTransport(
            new SlicServerTransport(colocTransport.ServerTransport),
            disposeSemaphore,
            refusedSemaphore);

        await using var server = new Server(
           new ServerOptions
           {
               ConnectionOptions = new ConnectionOptions { Dispatcher = dispatcher },
               MaxConnections = 1,
               ServerAddress = new ServerAddress(new Uri("icerpc://server"))
           },
           multiplexedServerTransport: serverTransport);

        server.Listen();

        await using var clientConnection1 = new ClientConnection(
           new ClientConnectionOptions() { ServerAddress = new ServerAddress(new Uri("icerpc://server")) },
           multiplexedClientTransport: new SlicClientTransport(colocTransport.ClientTransport));

        await using var clientConnection2 = new ClientConnection(
           new ClientConnectionOptions() { ServerAddress = new ServerAddress(new Uri("icerpc://server")) },
           multiplexedClientTransport: new SlicClientTransport(colocTransport.ClientTransport));

        await clientConnection1.ConnectAsync();

        try
        {
            await clientConnection2.ConnectAsync();
        }
        catch (ConnectionException)
        {
            // Connection refused
        }

        // Wait for the connection to began disposal.
        await disposeSemaphore.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        disposeSemaphore.Release(); // ensure disposing any future connections does not block

        await clientConnection1.ShutdownAsync();

        // Act

        // Dispose the server. This will wait for the background connection dispose to complete.
        ValueTask disposeTask = server.DisposeAsync();

        // Assert
        await Task.Delay(TimeSpan.FromSeconds(1));
        Assert.That(disposeTask.IsCompleted, Is.False);
        refusedSemaphore.Release();
        await disposeTask;
    }

    private class RefusedMultiplexServerTransport : IMultiplexedServerTransport
    {
        public string Name => _serverTransport.Name;
        private readonly SemaphoreSlim _disposeSemaphore;
        private readonly SemaphoreSlim _refusedSemaphore;

        private readonly IMultiplexedServerTransport _serverTransport;

        public RefusedMultiplexServerTransport(
            IMultiplexedServerTransport serverTransport,
            SemaphoreSlim disposeSemaphore,
            SemaphoreSlim refusedSemaphore)
        {
            _disposeSemaphore = disposeSemaphore;
            _refusedSemaphore = refusedSemaphore;
            _serverTransport = serverTransport;
        }

        public IListener<IMultiplexedConnection> Listen(
        ServerAddress serverAddress,
        MultiplexedConnectionOptions options,
        SslServerAuthenticationOptions? serverAuthenticationOptions) =>
            new RefusedConnectionListener(
                _serverTransport.Listen(serverAddress, options, serverAuthenticationOptions),
                _disposeSemaphore,
                _refusedSemaphore);
    }

    private class RefusedConnectionListener : IListener<IMultiplexedConnection>
    {
        public ServerAddress ServerAddress => _listener.ServerAddress;
        private readonly IListener<IMultiplexedConnection> _listener;
        private readonly SemaphoreSlim _disposeSemaphore;
        private readonly SemaphoreSlim _refusedSemaphore;

        public RefusedConnectionListener(
            IListener<IMultiplexedConnection> listener,
            SemaphoreSlim disposeSemaphore,
            SemaphoreSlim refusedSemaphore)
        {
            _listener = listener;
            _disposeSemaphore = disposeSemaphore;
            _refusedSemaphore = refusedSemaphore;
        }

        public async Task<(IMultiplexedConnection Connection, EndPoint RemoteNetworkAddress)>
        AcceptAsync(CancellationToken cancellationToken)
        {
            (IMultiplexedConnection connection, EndPoint endpoint) = await _listener.AcceptAsync(cancellationToken);
            return (new RefusedMultiplexedConneciton(connection, _disposeSemaphore, _refusedSemaphore), endpoint);
        }

        public ValueTask DisposeAsync() => _listener.DisposeAsync();
    }

    private class RefusedMultiplexedConneciton : IMultiplexedConnection
    {
        public ServerAddress ServerAddress => _connection.ServerAddress;

        private readonly IMultiplexedConnection _connection;
        private readonly SemaphoreSlim _disposeSemaphore;
        private readonly SemaphoreSlim _refusedSemaphore;

        public RefusedMultiplexedConneciton(
            IMultiplexedConnection connection,
            SemaphoreSlim disposeSemaphore,
            SemaphoreSlim refusedSemaphore)
        {
            _connection = connection;
            _disposeSemaphore = disposeSemaphore;
            _refusedSemaphore = refusedSemaphore;
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
            // The first connection dispose is the refused connection and will wait for the refused semaphore.
            if (_disposeSemaphore.CurrentCount == 0)
            {
                _disposeSemaphore.Release();
                await _refusedSemaphore.WaitAsync(CancellationToken.None).ConfigureAwait(false); ;
            }
            await _connection.DisposeAsync();
        }
    }
}
