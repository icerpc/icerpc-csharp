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
    [Test]
    public async Task Connection_idle_after_idle_timeout()
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

        using var reader = new DuplexConnectionReader(clientConnection, MemoryPool<byte>.Shared, 4096);
        reader.SetIdleTimeout(TimeSpan.FromMilliseconds(500));

        var startTime = TimeSpan.FromMilliseconds(Environment.TickCount64);

        // Act/Assert
        Assert.That(
            async () => await reader.ReadAsync(),
            Throws.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.ConnectionIdle));

        Assert.That(
            TimeSpan.FromMilliseconds(Environment.TickCount64) - startTime,
            Is.GreaterThan(TimeSpan.FromMilliseconds(490)));
    }

    [Test]
    public async Task Connection_idle_after_idle_timeout_defer()
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

        using var reader = new DuplexConnectionReader(clientConnection, MemoryPool<byte>.Shared, 4096);
        reader.SetIdleTimeout(TimeSpan.FromMilliseconds(500));

        // Write and read data to defer the idle timeout
        await serverConnection.WriteAsync(new ReadOnlyMemory<byte>[] { new byte[1] }, default);
        ReadOnlySequence<byte> buffer = await reader.ReadAsync(default);
        reader.AdvanceTo(buffer.End);

        var startTime = TimeSpan.FromMilliseconds(Environment.TickCount64);

        // Act/Assert
        Assert.That(
            async () => await reader.ReadAsync(),
            Throws.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.ConnectionIdle));

        Assert.That(
            TimeSpan.FromMilliseconds(Environment.TickCount64) - startTime,
            Is.GreaterThan(TimeSpan.FromMilliseconds(490)));
    }
}
