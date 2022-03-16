// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slice.Internal;
using NUnit.Framework;

namespace IceRpc.Slice.Tests;

[Parallelizable(scope: ParallelScope.All)]
[Timeout(30000)]
public class InvocationTests
{
    /// <summary>Verifies that the request context feature is set to the invocation context.</summary>
    [Test]
    public async Task Invocation_context()
    {
        // Arrange
        var invocation = new Invocation
        {
            Features = new FeatureCollection().WithContext(new Dictionary<string, string> { ["foo"] = "bar" })
        };

        IDictionary<string, string>? context = null;
        var proxy = new Proxy(Protocol.IceRpc)
        {
            Invoker = new InlineInvoker((request, cancel) =>
            {
                context = request.Features.GetContext();
                return Task.FromResult(new IncomingResponse(request));
            }),
        };

        // Act
        await proxy.InvokeAsync(
            "",
            Encoding.Slice20,
            EmptyPipeReader.Instance,
            null,
            SliceDecoder.GetActivator(typeof(InvocationTests).Assembly),
            invocation);

        // Assert
        Assert.That(context, Is.EqualTo(invocation.Features.GetContext()));
    }

    /// <summary>Verifies that the encoded deadline has the expected value when <see cref="Invocation.Deadline"/> is
    /// set.</summary>
    [Test]
    public async Task Invocation_deadline_sets_the_deadline_field()
    {
        // Arrange
        DateTime deadline = DateTime.MaxValue;
        var invoker = new InlineInvoker((request, cancel) =>
        {
            deadline = DecodeDeadlineField(request.Fields);
            return Task.FromResult(new IncomingResponse(request));
        });

        using var cancellationSource = new CancellationTokenSource();
        var sut = new Proxy(Protocol.IceRpc) { Invoker = invoker };
        DateTime expectedDeadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(50);
        var invocation = new Invocation() { Deadline = expectedDeadline };

        // Act
        await sut.InvokeAsync(
            "",
            Encoding.Slice20,
            EmptyPipeReader.Instance,
            null,
            SliceDecoder.GetActivator(typeof(InvocationTests).Assembly),
            invocation,
            cancel: cancellationSource.Token);

        // Assert
        Assert.That(Math.Abs((deadline - expectedDeadline).TotalMilliseconds), Is.LessThanOrEqualTo(1));
    }

    /// <summary>Verifies that the invocation is canceled after the <see cref="Invocation.Timeout"/> expires.</summary>
    [Test]
    public void Invocation_is_canceled_after_the_timeout_expires()
    {
        // Arrange
        CancellationToken? cancellationToken = null;
        bool hasDeadline = false;
        var sut = new Proxy(Protocol.IceRpc)
        {
            Invoker = new InlineInvoker(async (request, cancel) =>
            {
                hasDeadline = request.Fields.ContainsKey(RequestFieldKey.Deadline);
                cancellationToken = cancel;
                await Task.Delay(TimeSpan.FromSeconds(5), cancel);
                return new IncomingResponse(request);
            }),
        };

        // Act
        Assert.That(
            () => sut.InvokeAsync(
                "",
                Encoding.Slice20,
                EmptyPipeReader.Instance,
                null,
                SliceDecoder.GetActivator(typeof(InvocationTests).Assembly),
                new Invocation { Timeout = TimeSpan.FromMilliseconds(50) }),
            Throws.TypeOf<TaskCanceledException>());

        // Assert
        Assert.That(cancellationToken, Is.Not.Null);
        Assert.That(cancellationToken.Value.CanBeCanceled, Is.True);
        Assert.That(cancellationToken.Value.IsCancellationRequested, Is.True);
        Assert.That(hasDeadline, Is.True);
    }

    /// <summary>Verifies that the invocation timeout value set in the <see cref="Invocation.Timeout"/> prevails over
    /// the invocation timeout value previously set with the <see cref="TimeoutInterceptor"/>.</summary>
    [Test]
    public async Task Invocation_timeout_value_prevails_over_invoker_timeout_interceptor()
    {
        // Arrange
        var invocationTimeout = TimeSpan.FromSeconds(30);
        DateTime deadline = DateTime.MaxValue;
        DateTime expectedDeadline = DateTime.UtcNow + invocationTimeout;
        var invoker = new InlineInvoker((request, cancel) =>
        {
            deadline = DecodeDeadlineField(request.Fields);
            return Task.FromResult(new IncomingResponse(request));
        });
        var timeoutInterceptor = new TimeoutInterceptor(invoker, TimeSpan.FromSeconds(120));
        var sut = new Proxy(Protocol.IceRpc) { Invoker = timeoutInterceptor };

        // Act
        await sut.InvokeAsync(
            "",
            Encoding.Slice20,
            EmptyPipeReader.Instance,
            null,
            SliceDecoder.GetActivator(typeof(InvocationTests).Assembly),
            new Invocation() { Timeout = invocationTimeout });

        // Assert
        Assert.That(Math.Abs((deadline - expectedDeadline).TotalMilliseconds), Is.LessThanOrEqualTo(50));
    }

    /// <summary>Verifies that when using an infinite invocation timeout the cancellation token passed to the invoker
    /// is not cancelable.</summary>
    [Test]
    public async Task Invocation_with_an_infinite_timeout_uses_the_default_cancellation_token()
    {
        // Arrange
        CancellationToken? cancellationToken = null;
        bool hasDeadline = false;
        var sut = new Proxy(Protocol.IceRpc)
        {
            Invoker = new InlineInvoker((request, cancel) =>
            {
                cancellationToken = cancel;
                hasDeadline = request.Fields.ContainsKey(RequestFieldKey.Deadline);
                return Task.FromResult(new IncomingResponse(request));
            }),
        };

        // Act
        await sut.InvokeAsync(
            "",
            Encoding.Slice20,
            EmptyPipeReader.Instance,
            null,
            SliceDecoder.GetActivator(typeof(InvocationTests).Assembly),
            new Invocation { Timeout = Timeout.InfiniteTimeSpan });

        // Assert
        Assert.That(cancellationToken, Is.Not.Null);
        Assert.That(cancellationToken.Value.CanBeCanceled, Is.False);
        Assert.That(hasDeadline, Is.False);
    }

    /// <summary>Verifies that setting an invocation deadline requires providing a cancelable cancellation token.
    /// </summary>
    [Test]
    public void Setting_the_invocation_deadline_requires_a_cancelable_cancellation_token()
    {
        // Arrange
        var sut = new Proxy(Protocol.IceRpc);

        // Act/Assert
        Assert.ThrowsAsync<ArgumentException>(
            () => sut.InvokeAsync(
                "",
                Encoding.Slice20,
                EmptyPipeReader.Instance,
                null,
                SliceDecoder.GetActivator(typeof(InvocationTests).Assembly),
                new Invocation { Deadline = DateTime.Now + TimeSpan.FromSeconds(60) },
                cancel: CancellationToken.None));
    }

    private static DateTime DecodeDeadlineField(IDictionary<RequestFieldKey, OutgoingFieldValue> fields)
    {
        if (fields.TryGetValue(RequestFieldKey.Deadline, out var deadlineField))
        {
            var buffer = new byte[256];
            var bufferWriter = new MemoryBufferWriter(buffer);
            var encoder = new SliceEncoder(bufferWriter, Encoding.Slice20);
            deadlineField.Encode(ref encoder);
            var decoder = new SliceDecoder(buffer, Encoding.Slice20);
            decoder.SkipSize();
            long value = decoder.DecodeVarLong();
            return DateTime.UnixEpoch + TimeSpan.FromMilliseconds(value);
        }
        else
        {
            return DateTime.MaxValue;
        }
    }
}
