// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Tests.Common;
using IceRpc.Transports;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Buffers;
using System.Net;

namespace IceRpc.Tests.Transports;

[Parallelizable(scope: ParallelScope.All)]
public class DuplexConnectionReaderTests
{
    // TODO: Add more tests

    [Test]
    public async Task Ping_action_is_called()
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .UseDuplexTransport(new Uri("icerpc://colochost/"))
            .AddColocTransport()
            .BuildServiceProvider(validateScopes: true);

        var listener = provider.GetRequiredService<IListener<IDuplexConnection>>();
        await listener.ListenAsync(default);
        var clientConnection = provider.GetRequiredService<IDuplexConnection>();
        Task<(IDuplexConnection Connection, EndPoint RemoteNetworkAddress)> acceptTask = listener.AcceptAsync(default);
        Task<TransportConnectionInformation> clientConnectTask = clientConnection.ConnectAsync(default);
        using IDuplexConnection serverConnection = (await acceptTask).Connection;
        Task<TransportConnectionInformation> serverConnectTask = serverConnection.ConnectAsync(default);
        await Task.WhenAll(clientConnectTask, serverConnectTask);

        int pingCount = 0;
        using var reader = new DuplexConnectionReader(
            clientConnection,
            TimeSpan.FromMilliseconds(1000),
            MemoryPool<byte>.Shared,
            4096,
            connectionLostAction: _ => { },
            keepAliveAction: () => ++pingCount);
        reader.EnableIdleCheck();

        // Write and read data.
        await serverConnection.WriteAsync(new ReadOnlyMemory<byte>[] { new byte[1] }, default);
        ReadOnlySequence<byte> buffer = await reader.ReadAsync(default);
        reader.AdvanceTo(buffer.End);

        // Act
        // The ping action is called 500ms after a ReadAsync. We wait 900ms to ensure the ping action is called.
        await Task.Delay(TimeSpan.FromMilliseconds(900));

        // Assert
        Assert.That(pingCount, Is.EqualTo(1));
    }

    [Test]
    public async Task Abort_action_is_called_after_idle_timeout()
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .UseDuplexTransport(new Uri("icerpc://colochost/"))
            .AddColocTransport()
            .BuildServiceProvider(validateScopes: true);

        var listener = provider.GetRequiredService<IListener<IDuplexConnection>>();
        await listener.ListenAsync(default);
        var clientConnection = provider.GetRequiredService<IDuplexConnection>();
        Task<(IDuplexConnection Connection, EndPoint RemoteNetworkAddress)> acceptTask = listener.AcceptAsync(default);
        Task<TransportConnectionInformation> clientConnectTask = clientConnection.ConnectAsync(default);
        using IDuplexConnection serverConnection = (await acceptTask).Connection;
        Task<TransportConnectionInformation> serverConnectTask = serverConnection.ConnectAsync(default);
        await Task.WhenAll(clientConnectTask, serverConnectTask);

        TimeSpan abortCalledTime = Timeout.InfiniteTimeSpan;
        using var reader = new DuplexConnectionReader(
            clientConnection,
            TimeSpan.FromMilliseconds(500),
            MemoryPool<byte>.Shared,
            4096,
            connectionLostAction: _ => abortCalledTime = TimeSpan.FromMilliseconds(Environment.TickCount64),
            keepAliveAction: () => { });
        reader.EnableIdleCheck();

        // Act
        await Task.Delay(TimeSpan.FromSeconds(1));

        // Assert
        Assert.That(abortCalledTime, Is.GreaterThan(TimeSpan.FromMilliseconds(490)));
    }

    [Test]
    public async Task Abort_action_is_called_after_idle_timeout_defer()
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .UseDuplexTransport(new Uri("icerpc://colochost/"))
            .AddColocTransport()
            .BuildServiceProvider(validateScopes: true);

        var listener = provider.GetRequiredService<IListener<IDuplexConnection>>();
        await listener.ListenAsync(default);
        var clientConnection = provider.GetRequiredService<IDuplexConnection>();
        Task<(IDuplexConnection Connection, EndPoint RemoteNetworkAddress)> acceptTask = listener.AcceptAsync(default);
        Task<TransportConnectionInformation> clientConnectTask = clientConnection.ConnectAsync(default);
        using IDuplexConnection serverConnection = (await acceptTask).Connection;
        Task<TransportConnectionInformation> serverConnectTask = serverConnection.ConnectAsync(default);
        await Task.WhenAll(clientConnectTask, serverConnectTask);

        TimeSpan abortCalledTime = Timeout.InfiniteTimeSpan;
        using var reader = new DuplexConnectionReader(
            clientConnection,
            TimeSpan.FromMilliseconds(500),
            MemoryPool<byte>.Shared,
            4096,
            connectionLostAction: _ => abortCalledTime = TimeSpan.FromMilliseconds(Environment.TickCount64),
            keepAliveAction: () => { });
        reader.EnableIdleCheck();

        // Write and read data to defer the idle timeout
        await serverConnection.WriteAsync(new ReadOnlyMemory<byte>[] { new byte[1] }, default);
        ReadOnlySequence<byte> buffer = await reader.ReadAsync(default);
        reader.AdvanceTo(buffer.End);

        // Act
        await Task.Delay(TimeSpan.FromSeconds(1));

        // Assert
        Assert.That(abortCalledTime, Is.GreaterThan(TimeSpan.FromMilliseconds(250)));
    }
}
