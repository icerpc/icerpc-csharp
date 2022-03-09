// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using NUnit.Framework;

namespace IceRpc.Tests;
public class PipelineTests
{
    /// <summary>Verifies that an invoker cannot be added after
    /// <see cref="Pipeline.InvokeAsync(OutgoingRequest, CancellationToken)"/> has been called for the first time.
    /// </summary>
    [Test]
    public void Cannot_add_invoker_after_calling_invoke()
    {
        // Arrange
        var pipeline = new Pipeline();
            pipeline.Use(next => new InlineInvoker((request, cancel) =>
                Task.FromResult(new IncomingResponse(request))));
        pipeline.InvokeAsync(new OutgoingRequest(new Proxy(Protocol.IceRpc)));

        // Assert/Act
        Assert.Throws<InvalidOperationException>(
            () => pipeline.Use(next => new InlineInvoker((request, cancel) =>
                Task.FromResult(new IncomingResponse(request)))));
    }

    /// <summary>Verifies that the pipeline interceptors are called in the expected order. That corresponds
    /// to the order they were added to the pipeline.</summary>
    [Test]
    public void Pipeline_interceptors_call_order()
    {
        var calls = new List<string>();
        var expectedCalls = new List<string>() { "invoker-1", "invoker-2", "invoker-3", "invoker-4" };
        var pipeline = new Pipeline();
        pipeline.Use(
            next => new InlineInvoker((request, cancel) =>
            {
                calls.Add("invoker-1");
                return next.InvokeAsync(request, cancel);
            }),
            next => new InlineInvoker((request, cancel) =>
            {
                calls.Add("invoker-2");
                return next.InvokeAsync(request, cancel);
            }));

        pipeline.Use(
            next => new InlineInvoker((request, cancel) =>
            {
                calls.Add("invoker-3");
                return next.InvokeAsync(request, cancel);
            }),
            next => new InlineInvoker((request, cancel) =>
            {
                calls.Add("invoker-4");
                return Task.FromResult(new IncomingResponse(request));
            }));


        pipeline.InvokeAsync(new OutgoingRequest(new Proxy(Protocol.IceRpc)));

        Assert.That(calls, Is.EqualTo(expectedCalls));
    }

    /// <summary>Verifies that <see cref="Pipeline.With(Func{IInvoker, IInvoker}[])"/> can be used to create
    /// a new pipeline that contains the same interceptors as the source pipeline plus the interceptors passed
    /// to it.</summary>
    [Test]
    public void Clone_a_pipeline_using_with()
    {
        var calls = new List<string>();
        var expectedCalls = new List<string>() { "invoker-1", "invoker-2", "invoker-3" };
        var pipeline = new Pipeline();
        pipeline.Use(
            next => new InlineInvoker((request, cancel) =>
            {
                calls.Add("invoker-1");
                return next.InvokeAsync(request, cancel);
            }),
            next => new InlineInvoker((request, cancel) =>
            {
                calls.Add("invoker-2");
                return next.InvokeAsync(request, cancel);
            }));

        pipeline = pipeline.With(
            next => new InlineInvoker((request, cancel) =>
            {
                calls.Add("invoker-3");
                return Task.FromResult(new IncomingResponse(request));
            }));

        pipeline.InvokeAsync(new OutgoingRequest(new Proxy(Protocol.IceRpc)));

        Assert.That(calls, Is.EqualTo(expectedCalls));
    }
}
