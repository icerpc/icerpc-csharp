// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using NUnit.Framework;

namespace IceRpc.Slice.Tests;

[Parallelizable(scope: ParallelScope.All)]
[Timeout(30000)]
public class InvocationTests
{
    /// <summary>Verifies that the request context feature is set to the invocation context.
    /// </summary>
    /// <returns></returns>
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
            Path = "/",
            Invoker = new InlineInvoker((request, cancel) =>
            {
                context = request.Features.GetContext();
                return Task.FromResult(new IncomingResponse(request, ResultType.Success, EmptyPipeReader.Instance));
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

    // <summary>Verifies that the cancellation token passed to the invoker is canceled after the invocation timeout
    // expires.</summary>
    [Test]
    public void Invocation_timeout()
    {
        // Arrange
        var invocation = new Invocation
        {
            Timeout = TimeSpan.FromMilliseconds(500),
        };

        CancellationToken? cancelationToken = null;
        var proxy = new Proxy(Protocol.IceRpc)
        {
            Path = "/",
            Invoker = new InlineInvoker(async (request, cancel) =>
            {
                cancelationToken = cancel;
                await Task.Delay(10000, cancel);
                return new IncomingResponse(request, ResultType.Success, EmptyPipeReader.Instance);
            }),
        };

        // Act
        Assert.ThrowsAsync<TaskCanceledException>(
            () => proxy.InvokeAsync(
                "",
                Encoding.Slice20,
                EmptyPipeReader.Instance,
                null,
                SliceDecoder.GetActivator(typeof(InvocationTests).Assembly),
                invocation));

        // Assert
        Assert.That(cancelationToken, Is.Not.Null);
        Assert.That(cancelationToken.Value.CanBeCanceled, Is.True);
        Assert.That(cancelationToken.Value.IsCancellationRequested, Is.True);
    }

    // <summary>Verifies that when using an infinite invocation timeout the cancellation token passed to the invoker
    // cannot be canceled.</summary>
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
            Path = "/",
            Invoker = new InlineInvoker((request, cancel) =>
            {
                cancelationToken = cancel;
                return Task.FromResult(new IncomingResponse(request, ResultType.Success, EmptyPipeReader.Instance));
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
        Assert.That(cancelationToken, Is.Not.Null);
        Assert.That(cancelationToken.Value.CanBeCanceled, Is.False);
    }
}
