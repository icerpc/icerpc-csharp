// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Conformance.Tests;
using IceRpc.Tests.Common;
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
        using ClientServerSimpleTransportConnection sut = await provider.ConnectAndAcceptAsync();

        var activityTracker = new SimpleNetworkConnectionActivityTracker();
        using var reader = new SimpleNetworkConnectionReader(
            sut.ClientConnection,
            activityTracker,
            MemoryPool<byte>.Shared,
            4096);

        await sut.ServerConnection.WriteAsync(new ReadOnlyMemory<byte>[] { new byte[1] }, default);

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
