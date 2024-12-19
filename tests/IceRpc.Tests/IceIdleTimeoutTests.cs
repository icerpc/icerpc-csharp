// Copyright (c) ZeroC, Inc.

using IceRpc.Internal;
using IceRpc.Tests.Common;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Buffers;

namespace IceRpc.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class IceIdleTimeoutTests
{
    [Test]
    public async Task Ice_connection_idle_after_idle_timeout()
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddDuplexTransportTest()
            .AddColocTransport()
            .BuildServiceProvider(validateScopes: true);

        var sut = provider.GetRequiredService<ClientServerDuplexConnection>();
        await sut.AcceptAndConnectAsync();

        using var clientConnection = new IceDuplexConnectionDecorator(
            sut.Client,
            readIdleTimeout: TimeSpan.FromMilliseconds(500),
            writeIdleTimeout: TimeSpan.FromMilliseconds(500),
            sendHeartbeat: () => { });

        // Write and read data to the connection
        await sut.Server.WriteAsync(new ReadOnlySequence<byte>(new byte[1]), default);
        Memory<byte> buffer = new byte[1];
        await clientConnection.ReadAsync(buffer, default);

        var startTime = TimeSpan.FromMilliseconds(Environment.TickCount64);

        // Act/Assert
        Assert.That(
            async () => await clientConnection.ReadAsync(buffer, default),
            Throws.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.ConnectionIdle));

        Assert.That(
            TimeSpan.FromMilliseconds(Environment.TickCount64) - startTime,
            Is.GreaterThan(TimeSpan.FromMilliseconds(490)));
    }

    [Test]
    public async Task Ice_send_heartbeat_action_is_called()
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddDuplexTransportTest()
            .AddColocTransport()
            .BuildServiceProvider(validateScopes: true);

        var sut = provider.GetRequiredService<ClientServerDuplexConnection>();
        await sut.AcceptAndConnectAsync();

        using var semaphore = new SemaphoreSlim(0, 1);
        using var clientConnection = new IceDuplexConnectionDecorator(
            sut.Client,
            readIdleTimeout: Timeout.InfiniteTimeSpan,
            writeIdleTimeout: TimeSpan.FromMilliseconds(500),
            sendHeartbeat: () => semaphore.Release());

        // Write and read data.
        await clientConnection.WriteAsync(new ReadOnlySequence<byte>(new byte[1]), default);
        await sut.Server.ReadAsync(new byte[10], default);

        var startTime = TimeSpan.FromMilliseconds(Environment.TickCount64);

        // Act/Assert
        Assert.That(() => semaphore.WaitAsync(), Throws.Nothing);

        Assert.That(
            TimeSpan.FromMilliseconds(Environment.TickCount64) - startTime,
            Is.LessThan(TimeSpan.FromMilliseconds(500)));
    }

    /// <summary>Verifies that a connection where the client does not write anything is not aborted by the server
    /// connection idle monitor.</summary>
    /// <remarks>This test also verifies that the client idle monitor does not abort the connection when the server
    /// does not write anything; it's less interesting since the server always writes a ValidateConnection frame after
    /// accepting the connection from the client.</remarks>
    [Test][NonParallelizable]
    public async Task Server_idle_monitor_does_not_abort_connection_when_client_does_not_write_anything()
    {
        var connectionOptions = new ConnectionOptions
        {
            IceIdleTimeout = TimeSpan.FromMilliseconds(300)
        };

        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(
                Protocol.Ice,
                dispatcher: null,
                connectionOptions,
                connectionOptions)
            .BuildServiceProvider(validateScopes: true);

        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        (Task clientShutdownRequested, Task serverShutdownRequested) = await sut.ConnectAsync();

        // Act
        await Task.Delay(TimeSpan.FromMilliseconds(900)); // plenty of time for the idle monitor to kick in.

        // Assert
        Assert.That(serverShutdownRequested.IsCompleted, Is.False);
        Assert.That(clientShutdownRequested.IsCompleted, Is.False);

        // Graceful shutdown.
        Task clientShutdown = sut.Client.ShutdownAsync();
        Task serverShutdown = sut.Server.ShutdownAsync();
        Assert.That(async () => await Task.WhenAll(clientShutdown, serverShutdown), Throws.Nothing);
    }
}
