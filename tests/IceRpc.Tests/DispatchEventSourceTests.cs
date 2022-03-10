// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System.Diagnostics.Tracing;

namespace IceRpc.Tests;


public sealed class DispatchEventSourceTests
{
    [Test]
    public void Request_start_event_published()
    {
        int expectedEventId = 1;
        using var eventSource = new DispatchEventSource(Guid.NewGuid().ToString());
        using var eventListener = new TestEventListener(expectedEventId);
        eventListener.EnableEvents(eventSource, EventLevel.Verbose);

        eventSource.RequestStart(CreateIncomingRequest("/service", "ice_id"));

        EventWrittenEventArgs? eventData = eventListener.EventData;
        Assert.That(eventData, Is.Not.Null);
        Assert.That(eventData!.EventId, Is.EqualTo(expectedEventId));
        Assert.That(eventData.EventName, Is.EqualTo("RequestStart"));
        Assert.That(eventData.Level, Is.EqualTo(EventLevel.Informational));
        Assert.That(eventData.EventSource, Is.SameAs(eventSource));
        Assert.That(eventData.Payload![0], Is.EqualTo("/service"));
        Assert.That(eventData.Payload![1], Is.EqualTo("ice_id"));
    }

    [Test]
    public void Request_stop_event_published()
    {
        int expectedEventId = 2;
        using var eventListener = new TestEventListener(expectedEventId);
        using var eventSource = new DispatchEventSource(Guid.NewGuid().ToString());
        eventListener.EnableEvents(eventSource, EventLevel.Verbose);

        eventSource.RequestStop(CreateIncomingRequest("/service", "ice_id"));

        EventWrittenEventArgs? eventData = eventListener.EventData;
        Assert.That(eventData, Is.Not.Null);
        Assert.That(eventData!.EventId, Is.EqualTo(expectedEventId));
        Assert.That(eventData.EventName, Is.EqualTo("RequestStop"));
        Assert.That(eventData.Level, Is.EqualTo(EventLevel.Informational));
        Assert.That(eventData.EventSource, Is.SameAs(eventSource));
        Assert.That(eventData.Payload![0], Is.EqualTo("/service"));
        Assert.That(eventData.Payload![1], Is.EqualTo("ice_id"));
    }

    [Test]
    public void Request_canceled_event_published()
    {
        int expectedEventId = 3;
        using var eventListener = new TestEventListener(expectedEventId);
        using var eventSource = new DispatchEventSource(Guid.NewGuid().ToString());
        eventListener.EnableEvents(eventSource, EventLevel.Verbose);

        eventSource.RequestCanceled(CreateIncomingRequest("/service", "ice_id"));

        EventWrittenEventArgs? eventData = eventListener.EventData;
        Assert.That(eventData, Is.Not.Null);
        Assert.That(eventData!.EventId, Is.EqualTo(expectedEventId));
        Assert.That(eventData.EventName, Is.EqualTo("RequestCanceled"));
        Assert.That(eventData.Level, Is.EqualTo(EventLevel.Informational));
        Assert.That(eventData.EventSource, Is.SameAs(eventSource));
        Assert.That(eventData.Payload![0], Is.EqualTo("/service"));
        Assert.That(eventData.Payload![1], Is.EqualTo("ice_id"));
    }

    [Test]
    public void Request_failed_event_published()
    {
        int expectedEventId = 4;
        using var eventListener = new TestEventListener(expectedEventId);
        using var eventSource = new DispatchEventSource(Guid.NewGuid().ToString());
        eventListener.EnableEvents(eventSource, EventLevel.Verbose);

        eventSource.RequestFailed(CreateIncomingRequest("/service", "ice_id"), "IceRpc.RemoteException");

        EventWrittenEventArgs? eventData = eventListener.EventData;
        Assert.That(eventData, Is.Not.Null);
        Assert.That(eventData!.EventId, Is.EqualTo(expectedEventId));
        Assert.That(eventData.EventName, Is.EqualTo("RequestFailed"));
        Assert.That(eventData.Level, Is.EqualTo(EventLevel.Informational));
        Assert.That(eventData.EventSource, Is.SameAs(eventSource));
        Assert.That(eventData.Payload![0], Is.EqualTo("/service"));
        Assert.That(eventData.Payload![1], Is.EqualTo("ice_id"));
        Assert.That(eventData.Payload![2], Is.EqualTo("IceRpc.RemoteException"));
    }

    private static IncomingRequest CreateIncomingRequest(string path, string operation) =>
        new(Protocol.IceRpc)
        {
            Path = path,
            Operation = operation,
            PayloadEncoding = Encoding.Slice20
        };

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
