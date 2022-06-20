// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Tests.Common;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Deadline.Tests;

public sealed class DeadlineMiddlewareTests
{
    /// <summary>Verifies that the deadline decoded by the middleware has the expected value.</summary>
    [Test]
    public async Task Deadline_decoded_by_middleware_has_expected_value()
    {
        // Arrange
        DateTime deadline = DateTime.MaxValue;
        var dispatcher = new InlineDispatcher((request, cancel) =>
        {
            deadline = request.Features.Get<IDeadlineFeature>()?.Value ?? deadline;
            return new(new OutgoingResponse(request));
        });

        var sut = new DeadlineMiddleware(dispatcher);

        await using ServiceProvider provider = new ServiceCollection()
            .AddColocTest(sut)
            .BuildServiceProvider(validateScopes: true);

        provider.GetRequiredService<Server>().Listen();

        var proxy = Proxy.FromConnection(
            provider.GetRequiredService<ClientConnection>(),
            "/",
            invoker: new Pipeline().UseDeadline());

        DateTime expectedDeadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(100);
        var request = new OutgoingRequest(proxy)
        {
            Features = new FeatureCollection().With<IDeadlineFeature>(
                new DeadlineFeature { Value = expectedDeadline })
        };
        using var cancellationTokenSource = new CancellationTokenSource();

        // Act
        _ = await proxy.Invoker.InvokeAsync(request, cancellationTokenSource.Token);

        // Assert
        Assert.That(Math.Abs((deadline - expectedDeadline).TotalMilliseconds), Is.LessThanOrEqualTo(1));
    }
}
