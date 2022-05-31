// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Conformance.Tests;
using IceRpc.Tests.Common;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Buffers;

namespace IceRpc.Tests.Transports;

[Parallelizable(scope: ParallelScope.All)]
public class SimpleNetworkConnectionWriterTests
{
    // TODO: Add more tests

    /// <summary>Verifies that reading from the connection updates its last activity property.</summary>
    [Test]
    public async Task Write_updates_last_activity()
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .UseSimpleTransport("icerpc://colochost/")
            .AddColocTransport()
            .BuildServiceProvider();
        using ClientServerSimpleTransportConnection sut = await provider.ConnectAndAcceptAsync();

        var activityTracker = new SimpleNetworkConnectionActivityTracker();
        using var writer = new SimpleNetworkConnectionWriter(
            sut.ClientConnection,
            activityTracker,
            MemoryPool<byte>.Shared,
            4096);

        var delay = TimeSpan.FromMilliseconds(10);
        TimeSpan lastActivity = activityTracker.LastActivity;
        await Task.Delay(delay);

        // Act
        await writer.WriteAsync(new ReadOnlySequence<byte>(new byte[1]), default);

        // Assert
        Assert.That(
            activityTracker.LastActivity,
            Is.GreaterThanOrEqualTo(delay + lastActivity).Or.EqualTo(TimeSpan.Zero));
    }
}
