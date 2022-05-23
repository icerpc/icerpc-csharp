// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Features;
using IceRpc.Tests;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Deadline.Tests;

public sealed class DeadlineTests
{
    /// <summary>Verifies that setting a deadline requires providing a cancelable cancellation token.</summary>
    [Test]
    public void Setting_the_deadline_requires_a_cancelable_cancellation_token()
    {
        // Arrange
        var sut = new DeadlineInterceptor(Proxy.DefaultInvoker);
        var request = new OutgoingRequest(new Proxy(Protocol.IceRpc))
        {
            Features = new FeatureCollection().With<IDeadlineFeature>(
                new DeadlineFeature { Value = DateTime.UtcNow })
        };

        // Act/Assert
        Assert.That(
            () => sut.InvokeAsync(
                request,
                cancel: CancellationToken.None),
            Throws.TypeOf<InvalidOperationException>());
    }

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

        await using ServiceProvider provider = new SliceTestServiceCollection()
            .UseDispatcher(sut)
            .BuildServiceProvider();

        Proxy proxy = Proxy.FromConnection(
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
        await proxy.Invoker.InvokeAsync(request, cancellationTokenSource.Token);

        // Assert
        Assert.That(Math.Abs((deadline - expectedDeadline).TotalMilliseconds), Is.LessThanOrEqualTo(1));
    }
}
