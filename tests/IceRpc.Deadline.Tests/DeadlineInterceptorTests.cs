// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Tests.Common;
using NUnit.Framework;
using System.IO.Pipelines;
using ZeroC.Slice.Codec;
using Microsoft.Extensions.Time.Testing;

namespace IceRpc.Deadline.Tests;

[NonParallelizable]
public sealed class DeadlineInterceptorTests
{
    /// <summary>Verifies that the invocation throws TimeoutException when the invocation deadline expires.</summary>
    [Test]
    public void Invocation_fails_after_the_deadline_expires()
    {
        // Arrange
        CancellationToken? token = null;
        bool hasDeadline = false;

        var invoker = new InlineInvoker(async (request, cancellationToken) =>
        {
            hasDeadline = request.Fields.ContainsKey(RequestFieldKey.Deadline);
            token = cancellationToken;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new IncomingResponse(request, FakeConnectionContext.Instance);
        });

        var sut = new DeadlineInterceptor(
            invoker,
            defaultTimeout: TimeSpan.FromMilliseconds(10),
            alwaysEnforceDeadline: false);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));

        // Act
        Assert.ThrowsAsync<TimeoutException>(() => sut.InvokeAsync(request, CancellationToken.None));

        // Assert
        Assert.That(hasDeadline, Is.True);
        Assert.That(token, Is.Not.Null);
        Assert.That(token!.Value.CanBeCanceled, Is.True);
        Assert.That(token.Value.IsCancellationRequested, Is.True);
    }

    /// <summary>Verifies that the deadline value set in the <see cref="IDeadlineFeature" /> prevails over
    /// the default timeout value configured when installing the <see cref="DeadlineInterceptor" />.</summary>
    [Test]
    public async Task Deadline_feature_value_prevails_over_default_timeout()
    {
        // Arrange
        DateTime expectedDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);

        IFeatureCollection features = new FeatureCollection();
        features.Set<IDeadlineFeature>(new DeadlineFeature(expectedDeadline));

        DateTime deadline = DateTime.MaxValue;
        var sut = new DeadlineInterceptor(
            new InlineInvoker((request, cancellationToken) =>
            {
                if (request.Fields.TryGetValue(RequestFieldKey.Deadline, out OutgoingFieldValue deadlineFiled))
                {
                    deadline = ReadDeadline(deadlineFiled);
                }
                return Task.FromResult(new IncomingResponse(request, FakeConnectionContext.Instance));
            }),
            defaultTimeout: TimeSpan.FromSeconds(120),
            alwaysEnforceDeadline: false);

        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc))
        {
            Features = features
        };

        // Act
        await sut.InvokeAsync(request, default);

        // Assert
        Assert.That(deadline, Is.EqualTo(expectedDeadline));
    }

    /// <summary>Verifies that the deadline interceptor encodes the expected deadline value.</summary>
    [Test]
    public async Task Deadline_interceptor_sets_the_deadline_feature_from_timeout_value()
    {
        // Arrange
        var timeout = TimeSpan.FromMilliseconds(500);
        DateTime deadline = DateTime.MaxValue;
        var invoker = new InlineInvoker((request, cancellationToken) =>
        {
            if (request.Fields.TryGetValue(RequestFieldKey.Deadline, out OutgoingFieldValue deadlineFiled))
            {
                deadline = ReadDeadline(deadlineFiled);
            }
            return Task.FromResult(new IncomingResponse(request, FakeConnectionContext.Instance));
        });

        var sut = new DeadlineInterceptor(invoker, timeout, alwaysEnforceDeadline: false);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        DateTime expectedDeadline = DateTime.UtcNow + timeout;

        // Act
        await sut.InvokeAsync(request, default);

        // Assert
        Assert.That(Math.Abs((deadline - expectedDeadline).TotalMilliseconds), Is.LessThan(10));
    }

    [Test]
    public async Task Deadline_interceptor_does_not_enforce_deadline_by_default()
    {
        // Arrange
        CancellationToken? token = null;
        var invoker = new InlineInvoker((request, cancellationToken) =>
        {
            token = cancellationToken;
            return Task.FromResult(new IncomingResponse(request, FakeConnectionContext.Instance));
        });

        var timeProvider = new FakeTimeProvider();
        var sut = new DeadlineInterceptor(invoker, Timeout.InfiniteTimeSpan, alwaysEnforceDeadline: false, timeProvider);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc))
        {
            Features = new FeatureCollection().With<IDeadlineFeature>(
                new DeadlineFeature(timeProvider.GetUtcNow().UtcDateTime + TimeSpan.FromMilliseconds(100)))
        };
        using var cts = new CancellationTokenSource();

        // Act
        await sut.InvokeAsync(request, cts.Token);

        // Assert
        Assert.That(token, Is.Not.Null);
        Assert.That(token!.Value, Is.EqualTo(cts.Token));
    }

    [Test]
    public void Deadline_interceptor_can_enforce_application_deadline()
    {
        // Arrange
        var invoker = new InlineInvoker(async (request, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new IncomingResponse(request, FakeConnectionContext.Instance);
        });

        var timeProvider = new FakeTimeProvider();
        var sut = new DeadlineInterceptor(
            invoker,
            defaultTimeout: Timeout.InfiniteTimeSpan,
            alwaysEnforceDeadline: true,
            timeProvider);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc))
        {
            Features = new FeatureCollection().With<IDeadlineFeature>(
                new DeadlineFeature(timeProvider.GetUtcNow().UtcDateTime + TimeSpan.FromMilliseconds(100)))
        };
        using var tokenSource = new CancellationTokenSource();

        // Act/Assert
        Assert.ThrowsAsync<TimeoutException>(() => sut.InvokeAsync(request, tokenSource.Token));
    }

    /// <summary>Verifies that the interceptor encodes and enforces the same UTC instant regardless of the
    /// deadline feature value's <see cref="DateTime.Kind" /> (see issue #4421).</summary>
    [TestCase(DateTimeKind.Utc)]
    [TestCase(DateTimeKind.Local)]
    [TestCase(DateTimeKind.Unspecified)]
    public async Task Deadline_feature_is_normalized_to_utc(DateTimeKind kind)
    {
        // Arrange: a hardcoded UTC target instant expressed with different Kinds.
        DateTime targetUtc = new(2025, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        DateTime featureValue = kind switch
        {
            DateTimeKind.Utc => targetUtc,
            DateTimeKind.Local => targetUtc.ToLocalTime(),
            DateTimeKind.Unspecified => DateTime.SpecifyKind(targetUtc.ToLocalTime(), DateTimeKind.Unspecified),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        DateTime encodedDeadline = DateTime.MaxValue;
        var invoker = new InlineInvoker((request, cancellationToken) =>
        {
            if (request.Fields.TryGetValue(RequestFieldKey.Deadline, out OutgoingFieldValue deadlineField))
            {
                encodedDeadline = ReadDeadline(deadlineField);
            }
            return Task.FromResult(new IncomingResponse(request, FakeConnectionContext.Instance));
        });

        // Use a fake time provider so the interceptor doesn't trip the "deadline already expired" check on
        // the hardcoded target.
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(targetUtc - TimeSpan.FromMinutes(1), TimeSpan.Zero));
        var sut = new DeadlineInterceptor(invoker, Timeout.InfiniteTimeSpan, alwaysEnforceDeadline: false, timeProvider);

        IFeatureCollection features = new FeatureCollection();
        features.Set<IDeadlineFeature>(new DeadlineFeature(featureValue));
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc)) { Features = features };

        // Act
        await sut.InvokeAsync(request, default);

        // Assert
        Assert.That(encodedDeadline, Is.EqualTo(targetUtc));
    }

    private static DateTime ReadDeadline(OutgoingFieldValue field)
    {
        var pipe = new Pipe();
        field.WriteAction!(pipe.Writer);
        pipe.Writer.Complete();

        pipe.Reader.TryRead(out var readResult);
        var decoder = new SliceDecoder(readResult.Buffer);
        return decoder.DecodeTimeStamp();
    }
}
