// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;

namespace IceRpc.Tests;

[Parallelizable(ParallelScope.All)]
[Timeout(5000)]
public sealed class MetricsInterceptorTests
{
    /// <summary>Verifies that a canceled invocation published the expected events (request started, request canceled,
    /// and request stopped), using the provided invocation event source.</summary>
    [Test]
    public async Task Canceled_invocation_publishes_start_cancel_and_stop_events()
    {
        const string name = "Test.Canceled.Invocation.EventSource";
        var invoker = new InlineInvoker((request, cancel) => throw new OperationCanceledException());
        using var eventListener = new TestEventListener(
            name,
            ("total-requests", "1"),
            ("canceled-requests", "1"),
            ("current-requests", "0"));
        using var eventSource = new InvocationEventSource(name);
        var request = new OutgoingRequest(new Proxy(Protocol.IceRpc) { Path = "/" });
        var sut = new MetricsInterceptor(invoker, eventSource);

        try
        {
            await sut.InvokeAsync(request, default);
        }
        catch (OperationCanceledException)
        {
        }

        await eventListener.WaitForCounterEventsAsync();
        Assert.That(eventListener.ReceivedEventCounters, Is.EquivalentTo(eventListener.ExpectedEventCounters));
    }

    /// <summary>Verifies that a failed invocation published the expected events (request started, request failed,
    /// and request stopped), using the provided invocation event source.</summary>
    [Test]
    public async Task Failed_invocation_publishes_start_fail_and_stop_events()
    {
        const string name = "Test.Failed.Invocation.EventSource";
        var invoker = new InlineInvoker((request, cancel) => throw new InvalidOperationException());
        using var eventListener = new TestEventListener(
            name,
            ("total-requests", "1"),
            ("failed-requests", "1"),
            ("current-requests", "0"));
        using var eventSource = new InvocationEventSource(name);
        var request = new OutgoingRequest(new Proxy(Protocol.IceRpc) { Path = "/path" });
        var sut = new MetricsInterceptor(invoker, eventSource);

        try
        {
            await sut.InvokeAsync(request, default);
        }
        catch (InvalidOperationException)
        {
        }

        await eventListener.WaitForCounterEventsAsync();
        Assert.That(eventListener.ReceivedEventCounters, Is.EquivalentTo(eventListener.ExpectedEventCounters));
    }

    /// <summary>Verifies that a successful invocation published the expected events (request started, and request
    /// stopped), using the provided invocation event source.</summary>
    [Test]
    public async Task Successful_invocation_publishes_start_and_stop_events()
    {
        const string name = "Test.Succesful.Invocation.EventSource";
        var invoker = new InlineInvoker((request, cancel) => Task.FromResult(new IncomingResponse(request, request.Connection!)));
        using var eventListener = new TestEventListener(
            name,
            ("total-requests", "1"),
            ("current-requests", "0"));
        using var eventSource = new InvocationEventSource(name);
        var request = new OutgoingRequest(new Proxy(Protocol.IceRpc) { Path = "/path" });
        var sut = new MetricsInterceptor(invoker, eventSource);

        await sut.InvokeAsync(request, default);

        await eventListener.WaitForCounterEventsAsync();
        Assert.That(eventListener.ReceivedEventCounters, Is.EquivalentTo(eventListener.ExpectedEventCounters));
    }
}
