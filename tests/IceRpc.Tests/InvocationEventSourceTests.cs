// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System.Diagnostics.Tracing;

namespace IceRpc.Tests;

[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class InvocationEventSourceTests : IDisposable
{
    private readonly InvocationEventSource _eventSource;

    public InvocationEventSourceTests() =>
        _eventSource = new InvocationEventSource(Guid.NewGuid().ToString());

    [TearDown]
    public void TearDown() => Dispose();

    public void Dispose() => _eventSource.Dispose();

    [Test]
    public void InvocationEventSource_RequestStart()
    {
        int expectedEventId = 1;
        using var eventListener = new TestEventListener(expectedEventId);
        eventListener.EnableEvents(_eventSource, EventLevel.Verbose);

        var proxy = new Proxy(Protocol.IceRpc) { Path = "/service" };
        _eventSource.RequestStart(new OutgoingRequest(proxy, operation: "ice_id"));

        EventWrittenEventArgs? eventData = eventListener.EventData;
        Assert.That(eventData, Is.Not.Null);
        Assert.That(eventData!.EventId, Is.EqualTo(expectedEventId));
        Assert.That(eventData.EventName, Is.EqualTo("RequestStart"));
        Assert.That(eventData.Level, Is.EqualTo(EventLevel.Informational));
        Assert.That(eventData.EventSource, Is.SameAs(_eventSource));
        Assert.That(eventData.Payload![0], Is.EqualTo("/service"));
        Assert.That(eventData.Payload![1], Is.EqualTo("ice_id"));
    }

    [Test]
    public void InvocationEventSource_RequestStop()
    {
        int expectedEventId = 2;
        using var eventListener = new TestEventListener(expectedEventId);
        eventListener.EnableEvents(_eventSource, EventLevel.Verbose);

        var proxy = new Proxy(Protocol.IceRpc) { Path = "/service" };
        _eventSource.RequestStop(new OutgoingRequest(proxy, operation: "ice_id"));

        EventWrittenEventArgs? eventData = eventListener.EventData;
        Assert.That(eventData, Is.Not.Null);
        Assert.That(eventData!.EventId, Is.EqualTo(expectedEventId));
        Assert.That(eventData.EventName, Is.EqualTo("RequestStop"));
        Assert.That(eventData.Level, Is.EqualTo(EventLevel.Informational));
        Assert.That(eventData.EventSource, Is.SameAs(_eventSource));
        Assert.That(eventData.Payload![0], Is.EqualTo("/service"));
        Assert.That(eventData.Payload![1], Is.EqualTo("ice_id"));
    }

    [Test]
    public void InvocationEventSource_RequestCanceled()
    {
        int expectedEventId = 3;
        using var eventListener = new TestEventListener(expectedEventId);
        eventListener.EnableEvents(_eventSource, EventLevel.Verbose);

        var proxy = new Proxy(Protocol.IceRpc) { Path = "/service" };
        _eventSource.RequestCanceled(new OutgoingRequest(proxy, operation: "ice_id"));

        EventWrittenEventArgs? eventData = eventListener.EventData;
        Assert.That(eventData, Is.Not.Null);
        Assert.That(eventData!.EventId, Is.EqualTo(expectedEventId));
        Assert.That(eventData.EventName, Is.EqualTo("RequestCanceled"));
        Assert.That(eventData.Level, Is.EqualTo(EventLevel.Informational));
        Assert.That(eventData.EventSource, Is.SameAs(_eventSource));
        Assert.That(eventData.Payload![0], Is.EqualTo("/service"));
        Assert.That(eventData.Payload![1], Is.EqualTo("ice_id"));
    }

    [Test]
    public void InvocationEventSource_RequestFailed()
    {
        int expectedEventId = 4;
        using var eventListener = new TestEventListener(expectedEventId);
        eventListener.EnableEvents(_eventSource, EventLevel.Verbose);

        var proxy = new Proxy(Protocol.IceRpc) { Path = "/service" };
        _eventSource.RequestFailed(
            new OutgoingRequest(proxy, operation: "ice_id"),
            "IceRpc.RemoteException");

        EventWrittenEventArgs? eventData = eventListener.EventData;
        Assert.That(eventData, Is.Not.Null);
        Assert.That(eventData!.EventId, Is.EqualTo(expectedEventId));
        Assert.That(eventData.EventName, Is.EqualTo("RequestFailed"));
        Assert.That(eventData.Level, Is.EqualTo(EventLevel.Informational));
        Assert.That(eventData.EventSource, Is.SameAs(_eventSource));
        Assert.That(eventData.Payload![0], Is.EqualTo("/service"));
        Assert.That(eventData.Payload![1], Is.EqualTo("ice_id"));
        Assert.That(eventData.Payload![2], Is.EqualTo("IceRpc.RemoteException"));
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
