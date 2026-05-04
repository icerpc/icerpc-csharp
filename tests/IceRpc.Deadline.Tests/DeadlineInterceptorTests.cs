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

    /// <summary>Verifies the interceptor clamps an extreme-future IDeadlineFeature value instead of letting
    /// CancelAfter throw ArgumentOutOfRangeException.</summary>
    [Test]
    public async Task Invoke_with_extreme_future_deadline_does_not_throw()
    {
        // Arrange
        var invoker = new InlineInvoker((request, cancellationToken) =>
            Task.FromResult(new IncomingResponse(request, FakeConnectionContext.Instance)));

        var sut = new DeadlineInterceptor(invoker, Timeout.InfiniteTimeSpan, alwaysEnforceDeadline: true);
        // Close to DateTime.MaxValue but not equal, so the interceptor's "== DateTime.MaxValue" short-circuit
        // does not kick in and the CancelAfter path is actually exercised.
        DateTime extreme = DateTime.SpecifyKind(DateTime.MaxValue.AddDays(-1), DateTimeKind.Utc);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc))
        {
            Features = new FeatureCollection().With<IDeadlineFeature>(new DeadlineFeature(extreme))
        };

        // Act/Assert
        Assert.That(async () => await sut.InvokeAsync(request, default), Throws.Nothing);
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
    /// deadline feature value's <see cref="DateTime.Kind" />.</summary>
    [TestCaseSource(nameof(DeadlineFeatureIsNormalizedToUtcSource))]
    public async Task Deadline_feature_is_normalized_to_utc(DateTime deadlineValue)
    {
        // Arrange
        DateTime encodedDeadline = DateTime.MaxValue;
        var invoker = new InlineInvoker((request, cancellationToken) =>
        {
            if (request.Fields.TryGetValue(RequestFieldKey.Deadline, out OutgoingFieldValue deadlineField))
            {
                encodedDeadline = ReadDeadline(deadlineField);
            }
            return Task.FromResult(new IncomingResponse(request, FakeConnectionContext.Instance));
        });

        var timeProvider = new FakeTimeProvider(
            new DateTimeOffset(TargetUtc - TimeSpan.FromMinutes(1), TimeSpan.Zero));
        var sut = new DeadlineInterceptor(invoker, Timeout.InfiniteTimeSpan, alwaysEnforceDeadline: false, timeProvider);

        IFeatureCollection features = new FeatureCollection();
        features.Set<IDeadlineFeature>(new DeadlineFeature(deadlineValue));
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc)) { Features = features };

        // Act
        await sut.InvokeAsync(request, default);

        // Assert
        Assert.That(encodedDeadline, Is.EqualTo(TargetUtc));
    }

    private static readonly DateTime TargetUtc = new(2025, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    private static IEnumerable<TestCaseData> DeadlineFeatureIsNormalizedToUtcSource
    {
        get
        {
            yield return new TestCaseData(TargetUtc).SetName("Utc");
            yield return new TestCaseData(TargetUtc.ToLocalTime()).SetName("Local");
            yield return new TestCaseData(
                DateTime.SpecifyKind(TargetUtc.ToLocalTime(), DateTimeKind.Unspecified)).SetName("Unspecified");
        }
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

    [TestCase(0)]
    [TestCase(-5000)]
    public void Constructor_rejects_invalid_default_timeout(int milliseconds)
    {
        var invoker = new InlineInvoker((request, cancellationToken) =>
            Task.FromResult(new IncomingResponse(request, FakeConnectionContext.Instance)));

        Assert.That(
            () => new DeadlineInterceptor(invoker, TimeSpan.FromMilliseconds(milliseconds), alwaysEnforceDeadline: false),
            Throws.TypeOf<ArgumentException>());
    }

    /// <summary>Verifies the constructor rejects a default timeout beyond CancelAfter's supported maximum.
    /// TimeSpan.MaxValue would otherwise later cause DateTime overflow (now + timeout) or CancelAfter to throw
    /// ArgumentOutOfRangeException at first invoke.</summary>
    [Test]
    public void Constructor_rejects_default_timeout_beyond_cancel_after_max()
    {
        var invoker = new InlineInvoker((request, cancellationToken) =>
            Task.FromResult(new IncomingResponse(request, FakeConnectionContext.Instance)));

        Assert.That(
            () => new DeadlineInterceptor(invoker, TimeSpan.MaxValue, alwaysEnforceDeadline: false),
            Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void Constructor_accepts_infinite_timeout()
    {
        var invoker = new InlineInvoker((request, cancellationToken) =>
            Task.FromResult(new IncomingResponse(request, FakeConnectionContext.Instance)));

        Assert.That(
            () => new DeadlineInterceptor(invoker, Timeout.InfiniteTimeSpan, alwaysEnforceDeadline: false),
            Throws.Nothing);
    }
}
