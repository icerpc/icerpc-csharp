// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Diagnostics.Tracing;

namespace IceRpc.Tests.Internal
{
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    public class DispatchEventSourceTests
    {
        private DispatchEventSource _eventSource;

        public DispatchEventSourceTests() =>
            _eventSource = new DispatchEventSource(Guid.NewGuid().ToString());

        [Test]
        public void DispatchEventSource_RequestStart()
        {
            var expectedEventId = 1;
            var eventListener = new TestEventListener(expectedEventId);
            eventListener.EnableEvents(_eventSource, EventLevel.Verbose);

            _eventSource.RequestStart("/service", "operation");

            var eventData = eventListener.EventData;
            Assert.NotNull(eventData);
            Assert.AreEqual(expectedEventId, eventData!.EventId);
            Assert.AreEqual("RequestStart", eventData.EventName);
            Assert.AreEqual(EventLevel.Informational, eventData.Level);
            Assert.That(eventData.EventSource, Is.SameAs(_eventSource));
            Assert.AreEqual("/service", eventData.Payload![0]);
            Assert.AreEqual("operation", eventData.Payload![1]);
        }

        [Test]
        public void DispatchEventSource_RequestStop()
        {
            var expectedEventId = 2;
            var eventListener = new TestEventListener(expectedEventId);
            eventListener.EnableEvents(_eventSource, EventLevel.Verbose);

            _eventSource.RequestStop("/service", "operation");

            var eventData = eventListener.EventData;
            Assert.NotNull(eventData);
            Assert.AreEqual(expectedEventId, eventData!.EventId);
            Assert.AreEqual("RequestStop", eventData.EventName);
            Assert.AreEqual(EventLevel.Informational, eventData.Level);
            Assert.That(eventData.EventSource, Is.SameAs(_eventSource));
            Assert.AreEqual("/service", eventData.Payload![0]);
            Assert.AreEqual("operation", eventData.Payload![1]);
        }

        [Test]
        public void DispatchEventSource_RequestCanceled()
        {
            var expectedEventId = 3;
            var eventListener = new TestEventListener(expectedEventId);
            eventListener.EnableEvents(_eventSource, EventLevel.Verbose);

            _eventSource.RequestCanceled("/service", "operation");

            var eventData = eventListener.EventData;
            Assert.NotNull(eventData);
            Assert.AreEqual(expectedEventId, eventData!.EventId);
            Assert.AreEqual("RequestCanceled", eventData.EventName);
            Assert.AreEqual(EventLevel.Informational, eventData.Level);
            Assert.That(eventData.EventSource, Is.SameAs(_eventSource));
            Assert.AreEqual("/service", eventData.Payload![0]);
            Assert.AreEqual("operation", eventData.Payload![1]);
        }

        [Test]
        public void DispatchEventSource_RequestFailed()
        {
            var expectedEventId = 4;
            var eventListener = new TestEventListener(expectedEventId);
            eventListener.EnableEvents(_eventSource, EventLevel.Verbose);

            _eventSource.RequestFailed("/service", "operation", null);

            var eventData = eventListener.EventData;
            Assert.NotNull(eventData);
            Assert.AreEqual(expectedEventId, eventData!.EventId);
            Assert.AreEqual("RequestFailed", eventData.EventName);
            Assert.AreEqual(EventLevel.Informational, eventData.Level);
            Assert.That(eventData.EventSource, Is.SameAs(_eventSource));
            Assert.AreEqual("/service", eventData.Payload![0]);
            Assert.AreEqual("operation", eventData.Payload![1]);
            Assert.AreEqual("", eventData.Payload![2]);
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
}
