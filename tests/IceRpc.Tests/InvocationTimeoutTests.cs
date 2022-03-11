// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Internal;
using IceRpc.Slice;
using IceRpc.Slice.Internal;
using IceRpc.Transports;
using NUnit.Framework;

namespace IceRpc.Tests;

public sealed class InvocationTimeoutTests
{
    /// <summary>Verifies that <see cref="TimeoutInterceptor"/> doesn't allow to use an invalid invocation timeout
    /// value.</summary>
    /// <param name="timeout">The invalid timeout value for the test.</param>
    [TestCase(-2)] // Cannot use a negative timeout
    [TestCase(-1)] // Cannot use infinite timeout
    public void Cannot_use_an_invalid_timeout_with_the_timeout_interceptor(int timeout) =>
        // Act & Assert
        Assert.Throws<ArgumentException>(
            () => _ = new TimeoutInterceptor(Proxy.DefaultInvoker, TimeSpan.FromSeconds(timeout)));

    /// <summary>Verifies the timeout interceptor is correctly configured by calling
    /// <see cref="PipelineExtensions.UseTimeout(Pipeline, TimeSpan)"/>. The cancellation token provided to the
    /// invocation pipeline is canceled when the timeout expires.</summary>
    [Test]
    public void Invocation_cancellation_token_is_canceled_after_invocation_timeout_expires()
    {
        // Arrange
        CancellationToken? cancelationToken = null;
        bool hasDeadline = false;

        var invoker = new InlineInvoker(async (request, cancel) =>
            {
                hasDeadline = request.Fields.ContainsKey((int)FieldKey.Deadline);
                cancelationToken = cancel;
                await Task.Delay(10000, cancel);
                return new IncomingResponse(request);
            });

        var sut = new TimeoutInterceptor(invoker, TimeSpan.FromMilliseconds(500));
        var request = new OutgoingRequest(new Proxy(Protocol.IceRpc));

        // Act
        Assert.ThrowsAsync<TaskCanceledException>(() => sut.InvokeAsync(request, default));

        // Assert
        Assert.That(hasDeadline, Is.True);
        Assert.That(cancelationToken, Is.Not.Null);
        Assert.That(cancelationToken.Value.CanBeCanceled, Is.True);
        Assert.That(cancelationToken.Value.IsCancellationRequested, Is.True);
    }

    /// <summary>Verifies that the encoded deadline has the expected when using the timeout interceptor.</summary>
    [Test]
    public async Task Invocation_interceptor_sets_the_deadline_field()
    {
        // Arrange
        var timeout = TimeSpan.FromMilliseconds(500);
        DateTime deadline = DateTime.MaxValue;
        DateTime expectedDeadline = DateTime.UtcNow + timeout;
        var invoker = new InlineInvoker((request, cancel) =>
        {
            deadline = DecodeDeadlineField(request.Fields);
            return Task.FromResult(new IncomingResponse(request));
        });

        var sut = new TimeoutInterceptor(invoker, timeout);
        var request = new OutgoingRequest(new Proxy(Protocol.IceRpc));

        // Act
        await sut.InvokeAsync(request, default);

        // Assert
        Assert.That((deadline - expectedDeadline).TotalMilliseconds, Is.LessThan(100));
    }

    /// <summary>Verifies that the encoded deadline has the expected when using the timeout interceptor.</summary>
    [Test]
    public async Task Invocation_deadline_sets_the_deadline_field()
    {
        // Arrange
        var timeout = TimeSpan.FromMilliseconds(500);
        DateTime deadline = DateTime.MaxValue;
        DateTime expectedDeadline = DateTime.UtcNow + timeout;
        var invoker = new InlineInvoker((request, cancel) =>
        {
            deadline = DecodeDeadlineField(request.Fields);
            return Task.FromResult(new IncomingResponse(request));
        });

        using var cancelationSource = new CancellationTokenSource();
        var invocation = new Invocation() { Deadline = expectedDeadline };
        var sut = new Proxy(Protocol.IceRpc) { Invoker = invoker };

        // Act
        await sut.InvokeAsync("",
            Encoding.Slice20,
            EmptyPipeReader.Instance,
            null,
            SliceDecoder.GetActivator(typeof(InvocationTimeoutTests).Assembly),
            invocation,
            cancel: cancelationSource.Token);

        // Assert
        Assert.That((deadline - expectedDeadline).TotalMilliseconds, Is.LessThan(100));
    }

    /// <summary>Verifies that the invocation timeout value set in the <see cref="Invocation"/> prevails over the
    /// invocation timeout value set with <see cref="PipelineExtensions.UseTimeout(Pipeline, TimeSpan)"/>.</summary>
    [Test]
    public async Task Invocation_timeout_prevails_over_uset_timeout()
    {
        // Arrange
        var colocTransport = new ColocTransport();
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
            SliceDecoder.GetActivator(typeof(InvocationTimeoutTests).Assembly),
            new Invocation() { Timeout = invocationTimeout });

        // Assert
        Assert.That((deadline - expectedDeadline).TotalMilliseconds, Is.LessThan(100));
    }

    /// <summary>Verifies that when using an infinite invocation timeout the cancellation token passed to the invoker
    /// cannot be canceled.</summary>
    [Test]
    public async Task Invocation_with_infinite_timeout_cannot_be_canceled()
    {
        // Arrange
        CancellationToken? cancelationToken = null;
        bool hasDeadline = false;
        var sut = new Proxy(Protocol.IceRpc)
        {
            Invoker = new InlineInvoker((request, cancel) =>
            {
                cancelationToken = cancel;
                hasDeadline = request.Fields.ContainsKey((int)FieldKey.Deadline);
                return Task.FromResult(new IncomingResponse(request));
            }),
        };

        // Act
        await sut.InvokeAsync(
            "",
            Encoding.Slice20,
            EmptyPipeReader.Instance,
            null,
            SliceDecoder.GetActivator(typeof(InvocationTimeoutTests).Assembly),
            new Invocation { Timeout = Timeout.InfiniteTimeSpan });

        // Assert
        Assert.That(cancelationToken, Is.Not.Null);
        Assert.That(cancelationToken.Value.CanBeCanceled, Is.False);
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
                SliceDecoder.GetActivator(typeof(InvocationTimeoutTests).Assembly),
                new Invocation { Deadline = DateTime.Now + TimeSpan.FromSeconds(60) },
                cancel: CancellationToken.None));
    }

    /// <summary>Verifies that the timeout interceptor is automatically configured when <see cref="Invocation.Timeout"/> is
    /// set and <see cref="Invocation.Deadline"/> is not.</summary>
    [Test]
    public void Setting_the_invocation_timeout_configures_the_timeout_interceptor()
    {
        // Arrange
        CancellationToken? cancelationToken = null;
        bool hasDeadline = false;
        var sut = new Proxy(Protocol.IceRpc)
        {
            Invoker = new InlineInvoker(async (request, cancel) =>
            {
                hasDeadline = request.Fields.ContainsKey((int)FieldKey.Deadline);
                cancelationToken = cancel;
                await Task.Delay(10000, cancel);
                return new IncomingResponse(request);
            }),
        };

        // Act
        Assert.ThrowsAsync<TaskCanceledException>(
            () => sut.InvokeAsync(
                "",
                Encoding.Slice20,
                EmptyPipeReader.Instance,
                null,
                SliceDecoder.GetActivator(typeof(InvocationTimeoutTests).Assembly),
                new Invocation { Timeout = TimeSpan.FromMilliseconds(500) }));

        // Assert
        Assert.That(cancelationToken, Is.Not.Null);
        Assert.That(cancelationToken.Value.CanBeCanceled, Is.True);
        Assert.That(cancelationToken.Value.IsCancellationRequested, Is.True);
        Assert.That(hasDeadline, Is.True);
    }

    private static DateTime DecodeDeadlineField(IDictionary<int, OutgoingFieldValue> fields)
    {
        if (fields.TryGetValue((int)FieldKey.Deadline, out var deadlineField))
        {
            var buffer = new byte[256];
            var bufferWriter = new MemoryBufferWriter(buffer);
            var encoder = new SliceEncoder(bufferWriter, Encoding.Slice20);
            deadlineField.EncodeAction!.Invoke(ref encoder);
            var decoder = new SliceDecoder(buffer, Encoding.Slice20);
            return DateTime.UnixEpoch + TimeSpan.FromMilliseconds(decoder.DecodeVarLong());
        }
        else
        {
            return DateTime.MaxValue;
        }
    }
}
