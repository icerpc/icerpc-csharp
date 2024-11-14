// Copyright (c) ZeroC, Inc.

using IceRpc.Tests.Common;
using IceRpc.Transports.Slic.Internal;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Buffers;

namespace IceRpc.Tests.Transports.Slic;

[Parallelizable(scope: ParallelScope.All)]
public class SlicIdleTimeoutTests
{
    [Test]
    public async Task Slic_connection_idle_after_idle_timeout()
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddDuplexTransportTest()
            .AddColocTransport()
            .BuildServiceProvider(validateScopes: true);

        var sut = provider.GetRequiredService<ClientServerDuplexConnection>();
        await sut.AcceptAndConnectAsync();

        using var clientConnection = new SlicDuplexConnectionDecorator(sut.Client);
        clientConnection.Enable(TimeSpan.FromMilliseconds(500));

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
    [NonParallelizable]
    public async Task Slic_send_pings_are_called()
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddDuplexTransportTest()
            .AddColocTransport()
            .BuildServiceProvider(validateScopes: true);

        var sut = provider.GetRequiredService<ClientServerDuplexConnection>();
        await sut.AcceptAndConnectAsync();

        using var readSemaphore = new SemaphoreSlim(0, 1);
        using var writeSemaphore = new SemaphoreSlim(0, 1);

        using var clientConnection = new SlicDuplexConnectionDecorator(
            sut.Client,
            sendReadPing: () => readSemaphore.Release(),
            sendWritePing: () => writeSemaphore.Release());
        clientConnection.Enable(TimeSpan.FromMilliseconds(500));

        // Write and read data.
        await clientConnection.WriteAsync(new ReadOnlySequence<byte>(new byte[1]), default);
        await sut.Server.ReadAsync(new byte[10], default);

        var startTime = TimeSpan.FromMilliseconds(Environment.TickCount64);

        // Act/Assert
        Assert.That(readSemaphore.WaitAsync, Throws.Nothing);
        Assert.That(writeSemaphore.WaitAsync, Throws.Nothing);

        Assert.That(
            TimeSpan.FromMilliseconds(Environment.TickCount64) - startTime,
            Is.LessThan(TimeSpan.FromMilliseconds(500)));
    }
}
