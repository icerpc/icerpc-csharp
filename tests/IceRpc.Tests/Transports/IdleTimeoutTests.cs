// Copyright (c) ZeroC, Inc.

using IceRpc.Tests.Common;
using IceRpc.Transports;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Net;

namespace IceRpc.Tests.Transports;

[Parallelizable(scope: ParallelScope.All)]
public class IdleTimeoutTests
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

        clientConnection = new IdleTimeoutDuplexConnectionDecorator(clientConnection)
        {
            IdleTimeout = TimeSpan.FromMilliseconds(500)
        };

        // Write and read data to the connection
        await serverConnection.WriteAsync(new ReadOnlyMemory<byte>[] { new byte[1] }, default);
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

        clientConnection.Dispose();
    }
}
