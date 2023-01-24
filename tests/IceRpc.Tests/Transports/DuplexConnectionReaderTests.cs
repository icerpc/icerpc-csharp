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
    public async Task Abort_action_is_called_after_idle_timeout()
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddDuplexTransportClientServerTest(new Uri("icerpc://colochost/"))
            .AddColocTransport()
            .BuildServiceProvider(validateScopes: true);

        var listener = provider.GetRequiredService<IListener<IDuplexConnection>>();
        var clientConnection = provider.GetRequiredService<IDuplexConnection>();
        Task<(IDuplexConnection Connection, EndPoint RemoteNetworkAddress)> acceptTask = listener.AcceptAsync(default);
        Task<TransportConnectionInformation> clientConnectTask = clientConnection.ConnectAsync(default);
        using IDuplexConnection serverConnection = (await acceptTask).Connection;
        Task<TransportConnectionInformation> serverConnectTask = serverConnection.ConnectAsync(default);
        await Task.WhenAll(clientConnectTask, serverConnectTask);

        var tcs = new TaskCompletionSource<TimeSpan>();
        await using var reader = new DuplexConnectionReader(
            clientConnection,
            MemoryPool<byte>.Shared,
            4096,
            connectionIdleAction: () => tcs.SetResult(TimeSpan.FromMilliseconds(Environment.TickCount64)));
        var startTime = TimeSpan.FromMilliseconds(Environment.TickCount64);
        reader.EnableAliveCheck(TimeSpan.FromMilliseconds(500));

        // Act
        var abortCalledTime = await tcs.Task;

        // Assert
        Assert.That(abortCalledTime - startTime, Is.GreaterThan(TimeSpan.FromMilliseconds(490)));
    }

    [Test]
    public async Task Abort_action_is_called_after_idle_timeout_defer()
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddDuplexTransportClientServerTest(new Uri("icerpc://colochost/"))
            .AddColocTransport()
            .BuildServiceProvider(validateScopes: true);

        var listener = provider.GetRequiredService<IListener<IDuplexConnection>>();
        var clientConnection = provider.GetRequiredService<IDuplexConnection>();
        Task<(IDuplexConnection Connection, EndPoint RemoteNetworkAddress)> acceptTask = listener.AcceptAsync(default);
        Task<TransportConnectionInformation> clientConnectTask = clientConnection.ConnectAsync(default);
        using IDuplexConnection serverConnection = (await acceptTask).Connection;
        Task<TransportConnectionInformation> serverConnectTask = serverConnection.ConnectAsync(default);
        await Task.WhenAll(clientConnectTask, serverConnectTask);

        var tcs = new TaskCompletionSource<TimeSpan>();
        await using var reader = new DuplexConnectionReader(
            clientConnection,
            MemoryPool<byte>.Shared,
            4096,
            connectionIdleAction: () => tcs.SetResult(TimeSpan.FromMilliseconds(Environment.TickCount64)));
        var startTime = TimeSpan.FromMilliseconds(Environment.TickCount64);
        reader.EnableAliveCheck(TimeSpan.FromMilliseconds(500));

        // Write and read data to defer the idle timeout
        await serverConnection.WriteAsync(new ReadOnlyMemory<byte>[] { new byte[1] }, default);
        ReadOnlySequence<byte> buffer = await reader.ReadAsync(default);
        reader.AdvanceTo(buffer.End);

        // Act
        TimeSpan abortCalledTime = await tcs.Task;

        // Assert
        Assert.That(abortCalledTime - startTime, Is.GreaterThan(TimeSpan.FromMilliseconds(250)));
    }
}
