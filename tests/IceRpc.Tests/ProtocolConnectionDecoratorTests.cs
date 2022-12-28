// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Tests;

[Parallelizable(ParallelScope.All)]
public sealed class ProtocolConnectionDecoratorTests
{
    /// <summary>Verifies that the ShutdownTimeoutProtocolConnectionDecorator adds a shutdown timeout to ShutdownAsync.
    /// </summary>
    [Test]
    public async Task Shutdown_timeout_decorator_adds_shutdown_timeout()
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(Protocol.IceRpc)
            .BuildServiceProvider(validateScopes: true);

        ClientServerProtocolConnection pair = provider.GetRequiredService<ClientServerProtocolConnection>();
        await pair.ConnectAsync();
        await using IProtocolConnection sut =
            new ShutdownTimeoutProtocolConnectionDecorator(pair.Client, TimeSpan.FromMilliseconds(300));

        // Act/Assert
        Assert.That(async() => await sut.ShutdownAsync(), Throws.InstanceOf<TimeoutException>());
    }

    /// <summary>Verifies that the ShutdownTimeoutProtocolConnectionDecorator also supports regular shutdown
    /// cancellation.</summary>
    [Test]
    public async Task Shutdown_timeout_decorator_supports_shutdown_cancellation()
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(Protocol.IceRpc)
            .BuildServiceProvider(validateScopes: true);

        ClientServerProtocolConnection pair = provider.GetRequiredService<ClientServerProtocolConnection>();
        await pair.ConnectAsync();
        await using IProtocolConnection sut =
            new ShutdownTimeoutProtocolConnectionDecorator(pair.Client, TimeSpan.FromMilliseconds(300));

        using var cts = new CancellationTokenSource();
        Task shutdownTask = sut.ShutdownAsync(cts.Token);
        cts.Cancel();

        // Act/Assert
        Assert.That(async() => await shutdownTask, Throws.InstanceOf<OperationCanceledException>());
    }
}
