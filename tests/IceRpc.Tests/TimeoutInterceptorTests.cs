// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using IceRpc.Configure;
using IceRpc.Slice;
using IceRpc.Internal;

namespace IceRpc.Tests;

public sealed class TimeoutInterceptorTests
{
    /// <summary>Verifies that the timeout interceptor is automatically setup when <see cref="Invocation.Timeout"/> is
    /// set and <see cref="Invocation.Deadline"/> is not.</summary>
    [Test]
    public void Setup_timeout_interceptor_using_invocation_timeout()
    {
        // Arrange
        var invocation = new Invocation
        {
            Timeout = TimeSpan.FromMilliseconds(500),
        };

        CancellationToken? cancelationToken = null;
        bool hasDeadline = false;
        var proxy = new Proxy(Protocol.IceRpc)
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
            () => proxy.InvokeAsync(
                "",
                Encoding.Slice20,
                EmptyPipeReader.Instance,
                null,
                SliceDecoder.GetActivator(typeof(TimeoutInterceptorTests).Assembly),
                invocation));

        // Assert
        Assert.That(cancelationToken, Is.Not.Null);
        Assert.That(cancelationToken.Value.CanBeCanceled, Is.True);
        Assert.That(cancelationToken.Value.IsCancellationRequested, Is.True);
        Assert.That(hasDeadline, Is.True);
    }

    /// <summary>Verifies that the cancellation token passed to the invoker is canceled after the invocation timeout
    /// expires.</summary>
    [Test]
    public void Setup_timeout_interceptor_using_use_timeout()
    {
        // Arrange
        CancellationToken? cancelationToken = null;
        bool hasDeadline = false;
        var pipeline = new Pipeline();
        pipeline.UseTimeout(TimeSpan.FromMilliseconds(500));
        pipeline.Use(next =>
            new InlineInvoker(async (request, cancel) =>
            {
                hasDeadline = request.Fields.ContainsKey((int)FieldKey.Deadline);
                cancelationToken = cancel;
                await Task.Delay(10000, cancel);
                return new IncomingResponse(request);
            }));
        var proxy = new Proxy(Protocol.IceRpc)
        {
            Invoker = pipeline,
        };

        // Act
        Assert.ThrowsAsync<TaskCanceledException>(
            () => proxy.InvokeAsync(
                "",
                Encoding.Slice20,
                EmptyPipeReader.Instance,
                null,
                SliceDecoder.GetActivator(typeof(TimeoutInterceptorTests).Assembly),
                null));

        // Assert
        Assert.That(hasDeadline, Is.True);
        Assert.That(cancelationToken, Is.Not.Null);
        Assert.That(cancelationToken.Value.CanBeCanceled, Is.True);
        Assert.That(cancelationToken.Value.IsCancellationRequested, Is.True);
    }

    [Test]
    public void Cannot_set_invocation_deadline_with_a_non_cancellable_cancellation_token()
    {
        // Arrange
        var invocation = new Invocation
        {
            Deadline = DateTime.Now + TimeSpan.FromSeconds(60),
        };

        var proxy = new Proxy(Protocol.IceRpc);

        // Act
        Assert.ThrowsAsync<ArgumentException>(
            () => proxy.InvokeAsync(
                "",
                Encoding.Slice20,
                EmptyPipeReader.Instance,
                null,
                SliceDecoder.GetActivator(typeof(TimeoutInterceptorTests).Assembly),
                invocation,
                cancel: CancellationToken.None));
    }

    [TestCase(-2)] // Cannot use a negative timeout
    [TestCase(-1)] // Cannot use infinite timeout
    public void Invalid_timeout_with_timeout_interceptor(int timeout)
    {
        // Arrange
        var pipeline = new Pipeline();
        pipeline.UseTimeout(TimeSpan.FromSeconds(timeout));
        var proxy = new Proxy(Protocol.IceRpc)
        {
            Invoker = pipeline,
        };

        // Act
        Assert.ThrowsAsync<ArgumentException>(
            () => proxy.InvokeAsync(
                "",
                Encoding.Slice20,
                EmptyPipeReader.Instance,
                null,
                SliceDecoder.GetActivator(typeof(TimeoutInterceptorTests).Assembly),
                null));
    }

    /// <summary>Verifies that when using an infinite invocation timeout the cancellation token passed to the invoker
    /// cannot be canceled.</summary>
    [Test]
    public async Task Invocation_with_infinite_timeout_cannot_be_canceled()
    {
        // Arrange
        var invocation = new Invocation
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

        CancellationToken? cancelationToken = null;
        var proxy = new Proxy(Protocol.IceRpc)
        {
            Invoker = new InlineInvoker((request, cancel) =>
            {
                cancelationToken = cancel;
                return Task.FromResult(new IncomingResponse(request));
            }),
        };

        // Act
        await proxy.InvokeAsync(
            "",
            Encoding.Slice20,
            EmptyPipeReader.Instance,
            null,
            SliceDecoder.GetActivator(typeof(TimeoutInterceptorTests).Assembly),
            invocation);

        // Assert
        Assert.That(cancelationToken, Is.Not.Null);
        Assert.That(cancelationToken.Value.CanBeCanceled, Is.False);
    }
}
