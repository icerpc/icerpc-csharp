// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Deadline;
using IceRpc.Features;
using NUnit.Framework;

namespace IceRpc.Timeout.Tests;

public sealed class TimeoutInterceptorTests
{
    /// <summary>Verifies that the invocation is canceled when the invocation time expires.</summary>
    [Test]
    [NonParallelizable]
    public void Invocation_is_canceled_after_the_timeout_expires()
    {
        // Arrange
        CancellationToken? cancellationToken = null;
        bool hasDeadline = false;

        var invoker = new InlineInvoker(async (request, cancel) =>
        {
            hasDeadline = request.Features.Get<IDeadlineFeature>() is not null;
            cancellationToken = cancel;
            await Task.Delay(TimeSpan.FromMilliseconds(100), cancel);
            return new IncomingResponse(request, request.Connection!);
        });

        var sut = new TimeoutInterceptor(invoker, TimeSpan.FromMilliseconds(10));
        var request = new OutgoingRequest(new Proxy(Protocol.IceRpc));

        // Act
        Assert.ThrowsAsync<TaskCanceledException>(() => sut.InvokeAsync(request, default));

        // Assert
        Assert.That(hasDeadline, Is.True);
        Assert.That(cancellationToken, Is.Not.Null);
        Assert.That(cancellationToken.Value.CanBeCanceled, Is.True);
        Assert.That(cancellationToken.Value.IsCancellationRequested, Is.True);
    }

    /// <summary>Verifies that the timeout value set in the <see cref="ITimeoutFeature"/> prevails over
    /// the timeout value configured when installing the <see cref="TimeoutInterceptor"/>.</summary>
    [Test]
    [NonParallelizable]
    public async Task Timeout_feature_value_prevails_over_timeout_interceptor()
    {
        // Arrange
        var invocationTimeout = TimeSpan.FromSeconds(30);

        IFeatureCollection features = new FeatureCollection();
        features.Set<ITimeoutFeature>(new TimeoutFeature { Value = invocationTimeout });

        DateTime deadline = DateTime.MaxValue;
        DateTime expectedDeadline = DateTime.UtcNow + invocationTimeout;
        var sut = new TimeoutInterceptor(
            new InlineInvoker((request, cancel) =>
            {
                deadline = request.Features.Get<IDeadlineFeature>()?.Value ?? deadline;
                return Task.FromResult(new IncomingResponse(request, request.Connection!));
            }),
            TimeSpan.FromSeconds(120));

        var request = new OutgoingRequest(new Proxy(Protocol.IceRpc))
        {
            Features = features
        };

        // Act
        await sut.InvokeAsync(request, default);

        // Assert
        Assert.That(Math.Abs((deadline - expectedDeadline).TotalMilliseconds), Is.LessThan(10));
    }

    /// <summary>Verifies that the timeout interceptor encodes the expected deadline value.</summary>
    [Test]
    [NonParallelizable]
    public async Task Timeout_interceptor_sets_the_deadline_feature()
    {
        // Arrange
        var timeout = TimeSpan.FromMilliseconds(500);
        DateTime deadline = DateTime.MaxValue;
        var invoker = new InlineInvoker((request, cancel) =>
        {
            deadline = request.Features.Get<IDeadlineFeature>()?.Value ?? deadline;
            return Task.FromResult(new IncomingResponse(request, request.Connection!));
        });

        var sut = new TimeoutInterceptor(invoker, timeout);
        var request = new OutgoingRequest(new Proxy(Protocol.IceRpc));
        DateTime expectedDeadline = DateTime.UtcNow + timeout;

        // Act
        await sut.InvokeAsync(request, default);

        // Assert
        Assert.That(Math.Abs((deadline - expectedDeadline).TotalMilliseconds), Is.LessThan(10));
    }

    /// <summary>Verifies that <see cref="TimeoutInterceptor"/> doesn't allow using an invalid timeout value.</summary>
    /// <param name="timeout">The invalid timeout value for the test.</param>
    [TestCase(-2)] // Cannot use a negative timeout
    [TestCase(-1)] // Cannot use infinite timeout
    public void Timeout_interceptor_does_not_allow_invalid_timeout_values(int timeout) =>
        // Act & Assert
        Assert.Throws<ArgumentException>(
            () => _ = new TimeoutInterceptor(Proxy.DefaultInvoker, TimeSpan.FromSeconds(timeout)));
}
