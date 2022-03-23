// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;

namespace IceRpc.Tests;

[Parallelizable(ParallelScope.All)]
public sealed class MetricsMiddlewareTests
{
    /// <summary>Verifies that a canceled dispatch published the expected events (request started, request canceled,
    /// and request stopped), using the provided dispatch event source.</summary>
    [Test]
    public async Task Canceled_dispatch_publishes_start_cancel_and_stop_events()
    {
        const string name = "Test.Canceled.Dispatch.EventSource";
        var dispatcher = new InlineDispatcher((request, cancel) => throw new OperationCanceledException());
        using var eventListener = new TestEventListener(
            name,
            ("total-requests", "1"),
            ("canceled-requests", "1"),
            ("current-requests", "0"));
        using var eventSource = new DispatchEventSource(name);
        var request = new IncomingRequest(Protocol.IceRpc);
        var sut = new MetricsMiddleware(dispatcher, eventSource);

        try
        {
            await sut.DispatchAsync(request, default);
        }
        catch (OperationCanceledException)
        {
        }

        using var cancellationSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await eventListener.WaitForCounterEventsAsync(cancellationSource.Token);
        Assert.That(eventListener.ReceivedEventCounters, Is.EquivalentTo(eventListener.ExpectedEventCounters));
    }

    /// <summary>Verifies that a failed dispatch published the expected events (request started, request failed,
    /// and request stopped), using the provided dispatch event source.</summary>
    [Test]
    public async Task Failed_dispatch_publishes_start_fail_and_stop_events()
    {
        // Arrange
        const string name = "Test.Failed.Dispatch.EventSource";
        var dispatcher = new InlineDispatcher((request, cancel) => throw new InvalidOperationException());
        using var eventListener = new TestEventListener(
            name,
            ("total-requests", "1"),
            ("failed-requests", "1"),
            ("current-requests", "0"));
        using var eventSource = new DispatchEventSource(name);
        var request = new IncomingRequest(Protocol.IceRpc);
        var sut = new MetricsMiddleware(dispatcher, eventSource);

        try
        {
            await sut.DispatchAsync(request, default);
        }
        catch (InvalidOperationException)
        {
        }

        using var cancellationSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await eventListener.WaitForCounterEventsAsync(cancellationSource.Token);
        Assert.That(eventListener.ReceivedEventCounters, Is.EquivalentTo(eventListener.ExpectedEventCounters));
    }

    /// <summary>Verifies that a successful dispatch published the expected events (request started, and request
    /// stopped), using the provided dispatch event source.</summary>
    [Test]
    public async Task Successful_dispatch_publishes_start_and_stop_events()
    {
        const string name = "Test.Successful.Dispatch.EventSource";
        var dispatcher = new InlineDispatcher((request, cancel) => new(new OutgoingResponse(request)));
        using var eventListener = new TestEventListener(
            name,
            ("total-requests", "1"),
            ("current-requests", "0"));
        using var eventSource = new DispatchEventSource(name);
        var request = new IncomingRequest(Protocol.IceRpc);
        var sut = new MetricsMiddleware(dispatcher, eventSource);

        await sut.DispatchAsync(request, default);

        using var cancellationSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await eventListener.WaitForCounterEventsAsync(cancellationSource.Token);
        Assert.That(eventListener.ReceivedEventCounters, Is.EquivalentTo(eventListener.ExpectedEventCounters));
    }
}
