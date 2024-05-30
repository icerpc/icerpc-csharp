// Copyright (c) ZeroC, Inc.

using IceRpc.Tests.Common;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Buffers;

namespace IceRpc.Internal;

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
}
