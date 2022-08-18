// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal; // ServiceNotFoundDispatcher
using NUnit.Framework;

namespace IceRpc.Tests;

[Parallelizable(ParallelScope.All)]
public class ResumableClientConnectionTests
{
    [Test]
    public async Task Connection_can_reconnect_after_being_idle()
    {
        // Arrange
        var server = new Server(ServiceNotFoundDispatcher.Instance, new Uri("icerpc://127.0.0.1:0"));
        server.Listen();
        await using var connection = new ResumableClientConnection(
            new ClientConnectionOptions()
            {
                ServerAddress = server.ServerAddress,
                IdleTimeout = TimeSpan.FromMilliseconds(500)
            });
        await connection.ConnectAsync();

        using var semaphore = new SemaphoreSlim(0);
        connection.OnShutdown(message => semaphore.Release(1));
        await semaphore.WaitAsync();

        // Act/Assert
        Assert.That(async() => await connection.ConnectAsync(), Throws.Nothing);
    }

    [Test]
    public async Task Connection_can_reconnect_after_graceful_peer_shutdown()
    {
        // Arrange
        var server = new Server(ServiceNotFoundDispatcher.Instance, new Uri("icerpc://127.0.0.1:0"));
        server.Listen();
        ServerAddress serverAddress = server.ServerAddress;
        await using var connection = new ResumableClientConnection(serverAddress);
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
        server.Listen();
        ServerAddress serverAddress = server.ServerAddress;
        await using var connection = new ResumableClientConnection(serverAddress);
        await connection.ConnectAsync();
        try
        {
            // Cancel shutdown and dispose to abort the connection.
            await server.ShutdownAsync(new CancellationToken(true));
        }
        catch
        {
        }
        await server.DisposeAsync();

        using var semaphore = new SemaphoreSlim(0);
        connection.OnAbort(message => semaphore.Release(1));
        await semaphore.WaitAsync();

        server = new Server(ServiceNotFoundDispatcher.Instance, serverAddress);
        server.Listen();

        // Act/Assert
        Assert.That(async () => await connection.ConnectAsync(), Throws.Nothing);

        await server.DisposeAsync();
    }
}
