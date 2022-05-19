// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using IceRpc.Tests;
using IceRpc.Timeout;
using NUnit.Framework;

namespace IceRpc.Slice.Tests;

[Parallelizable(scope: ParallelScope.All)]
[Timeout(5000)]
public class InvocationTests
{
    /// <summary>Verifies that the invocation is canceled after the <see cref="Invocation.Timeout"/> expires.</summary>
    [Test]
    [NonParallelizable]
    public void Invocation_is_canceled_after_the_timeout_expires()
    {
        // Arrange
        CancellationToken? cancellationToken = null;
        bool hasDeadline = false;
        var sut = new ServicePrx(new Proxy(Protocol.IceRpc)
        {
            Invoker = new TimeoutInterceptor(
            new InlineInvoker(async (request, cancel) =>
            {
                hasDeadline = request.Fields.ContainsKey(RequestFieldKey.Deadline);
                cancellationToken = cancel;
                await Task.Delay(TimeSpan.FromMilliseconds(100), cancel);
                return new IncomingResponse(request, InvalidConnection.IceRpc);
            }),
            System.Threading.Timeout.InfiniteTimeSpan),
        });
        FeatureCollection features = new FeatureCollection().With<ITimeoutFeature>(
            new TimeoutFeature { Timeout = TimeSpan.FromMilliseconds(20) });

        // Act
        Assert.That(
            () => sut.InvokeAsync(
                "",
                SliceEncoding.Slice2,
                payload: null,
                payloadStream: null,
                defaultActivator: null,
                new Invocation { Features = features }),
            Throws.TypeOf<TaskCanceledException>());

        // Assert
        Assert.That(cancellationToken, Is.Not.Null);
        Assert.That(cancellationToken.Value.CanBeCanceled, Is.True);
        Assert.That(cancellationToken.Value.IsCancellationRequested, Is.True);
        Assert.That(hasDeadline, Is.True);
    }

    /// <summary>Verifies that the invocation timeout value set in the <see cref="ITimeoutFeature"/> prevails over
    /// the invocation timeout value previously set with the <see cref="TimeoutInterceptor"/>.</summary>
    [Test]
    [NonParallelizable]
    public async Task Invocation_timeout_value_prevails_over_invoker_timeout_interceptor()
    {
        // Arrange
        var invocationTimeout = TimeSpan.FromSeconds(30);
        FeatureCollection features = new FeatureCollection().With<ITimeoutFeature>(
            new TimeoutFeature { Timeout = invocationTimeout });
        DateTime deadline = DateTime.MaxValue;
        DateTime expectedDeadline = DateTime.UtcNow + invocationTimeout;
        var sut = new ServicePrx(new Proxy(Protocol.IceRpc)
        { 
            Invoker = new TimeoutInterceptor(
                new InlineInvoker((request, cancel) =>
                {
                    deadline = DecodeDeadlineField(request.Fields);
                    return Task.FromResult(new IncomingResponse(request, InvalidConnection.IceRpc));
                }),
                TimeSpan.FromSeconds(120))
        });

        // Act
        await sut.InvokeAsync(
            "",
            SliceEncoding.Slice2,
            payload: null,
            payloadStream: null,
            defaultActivator: null,
            new Invocation() { Features = features });

        // Assert
        Assert.That(Math.Abs((deadline - expectedDeadline).TotalMilliseconds), Is.LessThanOrEqualTo(10));
    }

    /// <summary>Verifies that when using an infinite invocation timeout the cancellation token passed to the invoker
    /// is not cancelable.</summary>
    [Test]
    public async Task Invocation_with_an_infinite_timeout_uses_the_default_cancellation_token()
    {
        // Arrange
        CancellationToken? cancellationToken = null;
        FeatureCollection features = new FeatureCollection().With<ITimeoutFeature>(
            new TimeoutFeature { Timeout = System.Threading.Timeout.InfiniteTimeSpan });
        bool hasDeadline = false;
        var sut = new ServicePrx(new Proxy(Protocol.IceRpc)
        {
            Invoker = new TimeoutInterceptor(
                new InlineInvoker((request, cancel) =>
                {
                    cancellationToken = cancel;
                    hasDeadline = request.Fields.ContainsKey(RequestFieldKey.Deadline);
                    return Task.FromResult(new IncomingResponse(request, InvalidConnection.IceRpc));
                }),
                System.Threading.Timeout.InfiniteTimeSpan),
        });

        // Act
        await sut.InvokeAsync(
            "",
            SliceEncoding.Slice2,
            payload: null,
            payloadStream: null,
            defaultActivator: null,
            new Invocation { Features = features });

        // Assert
        Assert.That(cancellationToken, Is.Not.Null);
        Assert.That(cancellationToken.Value.CanBeCanceled, Is.False);
        Assert.That(hasDeadline, Is.False);
    }

    private static DateTime DecodeDeadlineField(IDictionary<RequestFieldKey, OutgoingFieldValue> fields)
    {
        if (fields.TryGetValue(RequestFieldKey.Deadline, out var deadlineField))
        {
            var buffer = new byte[256];
            var bufferWriter = new MemoryBufferWriter(buffer);
            var encoder = new SliceEncoder(bufferWriter, SliceEncoding.Slice2);
            deadlineField.Encode(ref encoder);
            var decoder = new SliceDecoder(buffer, SliceEncoding.Slice2);
            decoder.SkipSize();
            long value = decoder.DecodeVarInt62();
            return DateTime.UnixEpoch + TimeSpan.FromMilliseconds(value);
        }
        else
        {
            return DateTime.MaxValue;
        }
    }
}
