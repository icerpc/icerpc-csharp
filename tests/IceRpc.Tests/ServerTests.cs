// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports;
using NUnit.Framework;

namespace IceRpc.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class ServerTests
{
    /// <summary>Verifies that calling <see cref="Server.ListenAsync"/> more than once fails with
    /// <see cref="InvalidOperationException"/> exception.</summary>
    [Test]
    public async Task Cannot_call_listen_twice()
    {
        await using var server = new Server(ServiceNotFoundDispatcher.Instance);
        await server.ListenAsync();

        Assert.ThrowsAsync<InvalidOperationException>(() => server.ListenAsync());
    }

    /// <summary>Verifies that calling <see cref="Server.ListenAsync"/> on a disposed server fails with
    /// <see cref="ObjectDisposedException"/>.</summary>
    [Test]
    public async Task Cannot_call_listen_on_a_disposed_server()
    {
        var server = new Server(ServiceNotFoundDispatcher.Instance);
        await server.DisposeAsync();

        Assert.ThrowsAsync<ObjectDisposedException>(() => server.ListenAsync());
    }

    /// <summary>Verifies that <see cref="Server.ShutdownComplete"/> task is completed after
    /// <see cref="Server.ShutdownAsync(CancellationToken)"/> completed.</summary>
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

        await server.ListenAsync();

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

        await server.ListenAsync();

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

        await using var connection3 = new ClientConnection(
            new ClientConnectionOptions
            {
                ServerAddress = server.ServerAddress,
            });

        await connection1.ConnectAsync();

        // Act/Assert
        Assert.ThrowsAsync<ConnectFailedException>(() => connection2.ConnectAsync());
        await connection1.ShutdownAsync();
        await connection3.ConnectAsync();
    }
}
