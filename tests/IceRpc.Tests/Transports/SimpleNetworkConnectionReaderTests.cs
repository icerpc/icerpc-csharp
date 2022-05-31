// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Tests.Common;
using IceRpc.Transports;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Buffers;

namespace IceRpc.Tests.Transports;

[Parallelizable(scope: ParallelScope.All)]
public class SimpleNetworkConnectionReaderTests
{
    // TODO: Add more tests

    /// <summary>Verifies that reading from the connection updates its last activity property.</summary>
    [Test]
    public async Task Read_updates_last_activity()
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .UseSimpleTransport("icerpc://colochost/")
            .AddColocTransport()
            .BuildServiceProvider();

        var listener = provider.GetRequiredService<IListener<ISimpleNetworkConnection>>();
        var clientConnection = provider.GetRequiredService<ISimpleNetworkConnection>();
        Task<ISimpleNetworkConnection> acceptTask = listener.AcceptAsync();
        Task<NetworkConnectionInformation> clientConnectTask = clientConnection.ConnectAsync(default);
        using ISimpleNetworkConnection serverConnection = await acceptTask;
        Task<NetworkConnectionInformation> serverConnectTask = serverConnection.ConnectAsync(default);
        await Task.WhenAll(clientConnectTask, serverConnectTask);

        var activityTracker = new SimpleNetworkConnectionActivityTracker();
        using var reader = new SimpleNetworkConnectionReader(
            clientConnection,
            activityTracker,
            MemoryPool<byte>.Shared,
            4096);

        await serverConnection.WriteAsync(new ReadOnlyMemory<byte>[] { new byte[1] }, default);

        TimeSpan lastActivity = activityTracker.LastActivity;
        var delay = TimeSpan.FromMilliseconds(2);
        await Task.Delay(delay);

        // Act
        await reader.ReadAsync(default);

        // Assert
        Assert.That(
            activityTracker.LastActivity,
            Is.GreaterThanOrEqualTo(delay + lastActivity).Or.EqualTo(TimeSpan.Zero));
    }
}
