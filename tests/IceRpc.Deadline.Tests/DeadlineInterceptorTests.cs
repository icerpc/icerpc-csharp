// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Slice;
using NUnit.Framework;
using System.IO.Pipelines;

namespace IceRpc.Deadline.Tests;

public sealed class DeadlineInterceptorTests
{
    /// <summary>Verifies that the invocation throws TimeoutException when the invocation deadline expires.</summary>
    [Test]
    [NonParallelizable]
    public void Invocation_fails_after_the_timeout_expires()
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

        var sut = new DeadlineInterceptor(invoker, new() { DefaultTimeout = TimeSpan.FromMilliseconds(10) });
        var request = new OutgoingRequest(new Proxy(Protocol.IceRpc));

        // Act
        Assert.ThrowsAsync<TimeoutException>(async () => await sut.InvokeAsync(request, CancellationToken.None));

        // Assert
        Assert.That(hasDeadline, Is.True);
        Assert.That(cancellationToken, Is.Not.Null);
        Assert.That(cancellationToken.Value.CanBeCanceled, Is.True);
        Assert.That(cancellationToken.Value.IsCancellationRequested, Is.True);
    }

    /// <summary>Verifies that the deadline value set in the <see cref="IDeadlineFeature"/> prevails over
    /// the default timeout value configured when installing the <see cref="DeadlineInterceptor"/>.</summary>
    [Test]
    [NonParallelizable]
    public async Task Deadline_feature_value_prevails_over_default_timeout()
    {
        // Arrange
        var invocationTimeout = TimeSpan.FromSeconds(30);

        IFeatureCollection features = new FeatureCollection();
        features.Set<IDeadlineFeature>(DeadlineFeature.FromTimeout(invocationTimeout));

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
            new() { DefaultTimeout = TimeSpan.FromSeconds(120) });

        var request = new OutgoingRequest(new Proxy(Protocol.IceRpc))
        {
            Features = features
        };

        // Act
        await sut.InvokeAsync(request, default);

        // Assert
        Assert.That(Math.Abs((deadline - expectedDeadline).TotalMilliseconds), Is.LessThan(10));
    }

    /// <summary>Verifies that the deadline interceptor encodes the expected deadline value.</summary>
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

        var sut = new DeadlineInterceptor(invoker, new() { DefaultTimeout = timeout });
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
