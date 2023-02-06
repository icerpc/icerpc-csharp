// Copyright (c) ZeroC, Inc.

using IceRpc.Tests.Common;
using IceRpc.Transports;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Buffers;
using System.Net;

namespace IceRpc.Tests.Transports;

[Parallelizable(scope: ParallelScope.All)]
public class DuplexConnectionWriterTests
{
    // TODO: Add more tests

    [Test]
    public async Task Ping_action_is_called()
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

        int pingCount = 0;
        await using var writer = new DuplexConnectionWriter(
            clientConnection,
            MemoryPool<byte>.Shared,
            4096,
            keepAliveAction: () => ++pingCount);
        writer.EnableKeepAlive(TimeSpan.FromMilliseconds(500));

        // Write and read data.
        await writer.WriteAsync(new ReadOnlySequence<byte>(new byte[1]), default);
        await serverConnection.ReadAsync(new byte[10], default);

        // Act
        // The ping action is called 500ms after a WriteAsync. We wait 900ms to ensure the ping action is called.
        await Task.Delay(TimeSpan.FromMilliseconds(900));

        // Assert
        Assert.That(pingCount, Is.EqualTo(1));
    }
}
