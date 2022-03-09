// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using NUnit.Framework;
using System.Buffers;
using System.Diagnostics.Tracing;
using System.IO.Pipelines;

namespace IceRpc.Tests;

[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class DispatchEventSourceTests : IDisposable
{
    private readonly DispatchEventSource _eventSource;

    public DispatchEventSourceTests() =>
        _eventSource = new DispatchEventSource(Guid.NewGuid().ToString());

    [TearDown]
    public void TearDown() => Dispose();

    public void Dispose() => _eventSource.Dispose();

    [Test]
    public void DispatchEventSource_RequestStart()
    {
        int expectedEventId = 1;
        using var eventListener = new TestEventListener(expectedEventId);
        eventListener.EnableEvents(_eventSource, EventLevel.Verbose);

        _eventSource.RequestStart(CreateIncomingRequest("/service", "ice_id"));

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
    public void DispatchEventSource_RequestStop()
    {
        int expectedEventId = 2;
        using var eventListener = new TestEventListener(expectedEventId);
        eventListener.EnableEvents(_eventSource, EventLevel.Verbose);

        _eventSource.RequestStop(CreateIncomingRequest("/service", "ice_id"));

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
    public void DispatchEventSource_RequestCanceled()
    {
        int expectedEventId = 3;
        using var eventListener = new TestEventListener(expectedEventId);
        eventListener.EnableEvents(_eventSource, EventLevel.Verbose);

        _eventSource.RequestCanceled(CreateIncomingRequest("/service", "ice_id"));

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
    public void DispatchEventSource_RequestFailed()
    {
        int expectedEventId = 4;
        using var eventListener = new TestEventListener(expectedEventId);
        eventListener.EnableEvents(_eventSource, EventLevel.Verbose);

        _eventSource.RequestFailed(CreateIncomingRequest("/service", "ice_id"), "IceRpc.RemoteException");

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
