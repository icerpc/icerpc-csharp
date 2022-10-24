// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Metrics.Internal;
using IceRpc.Tests.Common;
using NUnit.Framework;
using System.Diagnostics.Tracing;

namespace IceRpc.Metrics.Tests;

public sealed class DispatchEventSourceTests
{
    [Test]
    public void Request_start_event_published()
    {
        int expectedEventId = 1;
        using var eventSource = new DispatchEventSource(Guid.NewGuid().ToString());
        using var eventListener = new TestEventListener(expectedEventId);
        eventListener.EnableEvents(eventSource, EventLevel.Verbose);
        var request = new IncomingRequest(FakeConnectionContext.IceRpc)
        {
            Path = "/test",
            Operation = "Op"
        };

        eventSource.RequestStart(request);

        EventWrittenEventArgs? eventData = eventListener.EventData;
        Assert.That(eventData, Is.Not.Null);
        Assert.That(eventData!.EventId, Is.EqualTo(expectedEventId));
        Assert.That(eventData.EventName, Is.EqualTo("RequestStart"));
        Assert.That(eventData.Level, Is.EqualTo(EventLevel.Informational));
        Assert.That(eventData.EventSource, Is.SameAs(eventSource));
        Assert.That(eventData.Payload![0], Is.EqualTo(request.Path));
        Assert.That(eventData.Payload![1], Is.EqualTo(request.Operation));
    }

    [Test]
    public void Request_stop_event_published()
    {
        int expectedEventId = 2;
        using var eventListener = new TestEventListener(expectedEventId);
        using var eventSource = new DispatchEventSource(Guid.NewGuid().ToString());
        eventListener.EnableEvents(eventSource, EventLevel.Verbose);
        var request = new IncomingRequest(FakeConnectionContext.IceRpc)
        {
            Path = "/test",
            Operation = "Op"
        };

        eventSource.RequestStop(request, ResultType.Success, 0);

        EventWrittenEventArgs? eventData = eventListener.EventData;
        Assert.That(eventData, Is.Not.Null);
        Assert.That(eventData!.EventId, Is.EqualTo(expectedEventId));
        Assert.That(eventData.EventName, Is.EqualTo("RequestStop"));
        Assert.That(eventData.Level, Is.EqualTo(EventLevel.Informational));
        Assert.That(eventData.EventSource, Is.SameAs(eventSource));
        Assert.That(eventData.Payload![0], Is.EqualTo(request.Path));
        Assert.That(eventData.Payload![1], Is.EqualTo(request.Operation));
        Assert.That(eventData.Payload![2], Is.EqualTo((int)ResultType.Success));
    }

    [Test]
    public void Request_cancel_event_published()
    {
        int expectedEventId = 3;
        using var eventListener = new TestEventListener(expectedEventId);
        using var eventSource = new DispatchEventSource(Guid.NewGuid().ToString());
        eventListener.EnableEvents(eventSource, EventLevel.Verbose);
        var request = new IncomingRequest(FakeConnectionContext.IceRpc)
        {
            Path = "/test",
            Operation = "Op"
        };

        eventSource.RequestCancel(request);

        EventWrittenEventArgs? eventData = eventListener.EventData;
        Assert.That(eventData, Is.Not.Null);
        Assert.That(eventData!.EventId, Is.EqualTo(expectedEventId));
        Assert.That(eventData.EventName, Is.EqualTo("RequestCancel"));
        Assert.That(eventData.Level, Is.EqualTo(EventLevel.Informational));
        Assert.That(eventData.EventSource, Is.SameAs(eventSource));
        Assert.That(eventData.Payload![0], Is.EqualTo(request.Path));
        Assert.That(eventData.Payload![1], Is.EqualTo(request.Operation));
    }

    [Test]
    public void Request_failure_event_published()
    {
        int expectedEventId = 4;
        using var eventListener = new TestEventListener(expectedEventId);
        using var eventSource = new DispatchEventSource(Guid.NewGuid().ToString());
        eventListener.EnableEvents(eventSource, EventLevel.Verbose);
        var request = new IncomingRequest(FakeConnectionContext.IceRpc)
        {
            Path = "/test",
            Operation = "Op"
        };
        var ex = new InvalidOperationException("op");

        eventSource.RequestFailure(request, ex);

        EventWrittenEventArgs? eventData = eventListener.EventData;
        Assert.That(eventData, Is.Not.Null);
        Assert.That(eventData!.EventId, Is.EqualTo(expectedEventId));
        Assert.That(eventData.EventName, Is.EqualTo("RequestFailure"));
        Assert.That(eventData.Level, Is.EqualTo(EventLevel.Error));
        Assert.That(eventData.EventSource, Is.SameAs(eventSource));
        Assert.That(eventData.Payload![0], Is.EqualTo(request.Path));
        Assert.That(eventData.Payload![1], Is.EqualTo(request.Operation));
        Assert.That(eventData.Payload![2], Is.EqualTo(ex.GetType().FullName));
        Assert.That(eventData.Payload![3], Is.EqualTo(ex.ToString()));
    }

    private class TestEventListener : EventListener
    {
        private readonly int _eventId;

        public TestEventListener(int eventId) => _eventId = eventId;

        public EventWrittenEventArgs? EventData { get; private set; }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            if (eventData.EventId == _eventId)
            {
                EventData = eventData;
            }
        }
    }
}
