// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Tests.Common;
using NUnit.Framework;

namespace IceRpc.Tests;

public class PipelineTests
{
    /// <summary>Verifies that an invoker cannot be added after
    /// <see cref="Pipeline.InvokeAsync(OutgoingRequest, CancellationToken)" /> has been called for the first time.
    /// </summary>
    [Test]
    public void Cannot_add_invoker_after_calling_invoke()
    {
        // Arrange
        var pipeline = new Pipeline()
            .Use(next => new InlineInvoker((request, cancellationToken) =>
                Task.FromResult(new IncomingResponse(request, FakeConnectionContext.Instance))))
            .Into(VoidInvoker.Instance);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        pipeline.InvokeAsync(request);

        // Assert/Act
        Assert.Throws<InvalidOperationException>(
            () => pipeline.Use(next => new InlineInvoker((request, cancellationToken) =>
                Task.FromResult(new IncomingResponse(request, FakeConnectionContext.Instance)))));
    }

    /// <summary>Verifies that the pipeline interceptors are called in the expected order. That corresponds
    /// to the order they were added to the pipeline.</summary>
    [Test]
    public void Pipeline_interceptors_call_order()
    {
        var calls = new List<string>();
        var expectedCalls = new List<string>() { "invoker-1", "invoker-2", "invoker-3", "invoker-4" };
        var pipeline = new Pipeline();
        pipeline
            .Use(next => new InlineInvoker((request, cancellationToken) =>
                {
                    calls.Add("invoker-1");
                    return next.InvokeAsync(request, cancellationToken);
                }))
            .Use(next => new InlineInvoker((request, cancellationToken) =>
                {
                    calls.Add("invoker-2");
                    return next.InvokeAsync(request, cancellationToken);
                }))
            .Into(VoidInvoker.Instance);

        pipeline
            .Use(next => new InlineInvoker((request, cancellationToken) =>
                {
                    calls.Add("invoker-3");
                    return next.InvokeAsync(request, cancellationToken);
                }))
            .Use(next => new InlineInvoker((request, cancellationToken) =>
                {
                    calls.Add("invoker-4");
                    return Task.FromResult(new IncomingResponse(request, FakeConnectionContext.Instance));
                }))
             .Into(VoidInvoker.Instance);

        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        pipeline.InvokeAsync(request);

        Assert.That(calls, Is.EqualTo(expectedCalls));
    }

    /// <summary>Verifies that the pipeline sets the request features.</summary>
    [Test]
    public void Use_feature()
    {
        // Arrange
        const string expected = "foo";
        var pipeline = new Pipeline();

        // Act
        pipeline.UseFeature(expected).Into(VoidInvoker.Instance);

        // Assert
        string? feature = null;
        pipeline.Use(next => new InlineInvoker((request, cancellationToken) =>
        {
            feature = request.Features.Get<string>();
            return Task.FromResult(new IncomingResponse(request, FakeConnectionContext.Instance));
        }));
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        pipeline.InvokeAsync(request);
        Assert.That(feature, Is.EqualTo(expected));
    }
}
