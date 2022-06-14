// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Slice;
using IceRpc.Tests.Common;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.IO.Pipelines;

namespace IceRpc.Deadline.Tests;

public sealed class DeadlineTests
{
    /// <summary>Verifies that setting a deadline requires providing a cancelable cancellation token.</summary>
    [Test]
    public void Setting_the_deadline_requires_a_cancelable_cancellation_token()
    {
        // Arrange
        var sut = new DeadlineInterceptor(Proxy.DefaultInvoker, Timeout.InfiniteTimeSpan);
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
            hasDeadline = request.Fields.ContainsKey(RequestFieldKey.Deadline);
            cancellationToken = cancel;
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancel);
            return new IncomingResponse(request, request.Connection!);
        });

        var sut = new DeadlineInterceptor(invoker, TimeSpan.FromMilliseconds(10));
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
    /// the timeout value configured when installing the <see cref="DeadlineInterceptor"/>.</summary>
    [Test]
    [NonParallelizable]
    public async Task Timeout_feature_value_prevails_over_default_timeout()
    {
        // Arrange
        var invocationTimeout = TimeSpan.FromSeconds(30);

        IFeatureCollection features = new FeatureCollection();
        features.Set<ITimeoutFeature>(new TimeoutFeature { Value = invocationTimeout });

        DateTime deadline = DateTime.MaxValue;
        DateTime expectedDeadline = DateTime.UtcNow + invocationTimeout;
        var sut = new DeadlineInterceptor(
            new InlineInvoker((request, cancel) =>
            {
                if (request.Fields.TryGetValue(RequestFieldKey.Deadline, out OutgoingFieldValue deadlineFiled))
                {
                    deadline = ReadDeadline(deadlineFiled);
                }
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
    public async Task Deadline_interceptor_sets_the_deadline_feature_from_timeout_value()
    {
        // Arrange
        var timeout = TimeSpan.FromMilliseconds(500);
        DateTime deadline = DateTime.MaxValue;
        var invoker = new InlineInvoker((request, cancel) =>
        {
            if (request.Fields.TryGetValue(RequestFieldKey.Deadline, out OutgoingFieldValue deadlineFiled))
            {
                deadline = ReadDeadline(deadlineFiled);
            }
            return Task.FromResult(new IncomingResponse(request, request.Connection!));
        });

        var sut = new DeadlineInterceptor(invoker, timeout);
        var request = new OutgoingRequest(new Proxy(Protocol.IceRpc));
        DateTime expectedDeadline = DateTime.UtcNow + timeout;

        // Act
        await sut.InvokeAsync(request, default);

        // Assert
        Assert.That(Math.Abs((deadline - expectedDeadline).TotalMilliseconds), Is.LessThan(10));
    }

    private static DateTime ReadDeadline(OutgoingFieldValue field)
    {
        var pipe = new Pipe();
        var encoder = new SliceEncoder(pipe.Writer, SliceEncoding.Slice2);
        field.Encode(ref encoder);
        pipe.Writer.Complete();

        pipe.Reader.TryRead(out var readResult);
        var decoder = new SliceDecoder(readResult.Buffer, SliceEncoding.Slice2);
        decoder.SkipSize();
        return DateTime.UnixEpoch + TimeSpan.FromMilliseconds(decoder.DecodeVarInt62());
    }
}
