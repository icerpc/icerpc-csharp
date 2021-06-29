// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Diagnostics.Tracing;

namespace IceRpc.Tests.Internal
{
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    public class DispatchEventSourceTests
    {
        private readonly DispatchEventSource _eventSource;

        public DispatchEventSourceTests() =>
            _eventSource = new DispatchEventSource(Guid.NewGuid().ToString());

        [Test]
        public void DispatchEventSource_RequestStart()
        {
            int expectedEventId = 1;
            var eventListener = new TestEventListener(expectedEventId);
            eventListener.EnableEvents(_eventSource, EventLevel.Verbose);

            var prx = IServicePrx.Parse("ice+tcp://localhost/service");

            var outgoingRequest = new OutgoingRequest(prx,
                                                      "ice_id",
                                                      Payload.FromEmptyArgs(prx),
                                                      null,
                                                      DateTime.MaxValue);

            var request = new IncomingRequest(outgoingRequest);
            _eventSource.RequestStart(request);

            EventWrittenEventArgs? eventData = eventListener.EventData;
            Assert.That(eventData, Is.Not.Null);
            Assert.AreEqual(expectedEventId, eventData!.EventId);
            Assert.AreEqual("RequestStart", eventData.EventName);
            Assert.AreEqual(EventLevel.Informational, eventData.Level);
            Assert.That(eventData.EventSource, Is.SameAs(_eventSource));
            Assert.AreEqual("/service", eventData.Payload![0]);
            Assert.AreEqual("ice_id", eventData.Payload![1]);
        }

        [Test]
        public void DispatchEventSource_RequestStop()
        {
            int expectedEventId = 2;
            var eventListener = new TestEventListener(expectedEventId);
            eventListener.EnableEvents(_eventSource, EventLevel.Verbose);

            var prx = IServicePrx.Parse("ice+tcp://localhost/service");
            var outgoingRequest = new OutgoingRequest(prx,
                                                      "ice_id",
                                                      Payload.FromEmptyArgs(prx),
                                                      null,
                                                      DateTime.MaxValue);

            var request = new IncomingRequest(outgoingRequest);
            _eventSource.RequestStop(request);

            EventWrittenEventArgs? eventData = eventListener.EventData;
            Assert.That(eventData, Is.Not.Null);
            Assert.AreEqual(expectedEventId, eventData!.EventId);
            Assert.AreEqual("RequestStop", eventData.EventName);
            Assert.AreEqual(EventLevel.Informational, eventData.Level);
            Assert.That(eventData.EventSource, Is.SameAs(_eventSource));
            Assert.AreEqual("/service", eventData.Payload![0]);
            Assert.AreEqual("ice_id", eventData.Payload![1]);
        }

        [Test]
        public void DispatchEventSource_RequestCanceled()
        {
            int expectedEventId = 3;
            var eventListener = new TestEventListener(expectedEventId);
            eventListener.EnableEvents(_eventSource, EventLevel.Verbose);

            var prx = IServicePrx.Parse("ice+tcp://localhost/service");
            var outgoingRequest = new OutgoingRequest(prx,
                                                      "ice_id",
                                                      Payload.FromEmptyArgs(prx),
                                                      null,
                                                      DateTime.MaxValue);

            var request = new IncomingRequest(outgoingRequest);
            _eventSource.RequestCanceled(request);

            EventWrittenEventArgs? eventData = eventListener.EventData;
            Assert.That(eventData, Is.Not.Null);
            Assert.AreEqual(expectedEventId, eventData!.EventId);
            Assert.AreEqual("RequestCanceled", eventData.EventName);
            Assert.AreEqual(EventLevel.Informational, eventData.Level);
            Assert.That(eventData.EventSource, Is.SameAs(_eventSource));
            Assert.AreEqual("/service", eventData.Payload![0]);
            Assert.AreEqual("ice_id", eventData.Payload![1]);
        }

        [Test]
        public void DispatchEventSource_RequestFailed()
        {
            int expectedEventId = 4;
            var eventListener = new TestEventListener(expectedEventId);
            eventListener.EnableEvents(_eventSource, EventLevel.Verbose);

            var prx = IServicePrx.Parse("ice+tcp://localhost/service");
            var outgoingRequest = new OutgoingRequest(prx,
                                                      "ice_id",
                                                      Payload.FromEmptyArgs(prx),
                                                      null,
                                                      DateTime.MaxValue);

            var request = new IncomingRequest(outgoingRequest);
            _eventSource.RequestFailed(request, "IceRpc.RemoteException");

            EventWrittenEventArgs? eventData = eventListener.EventData;
            Assert.That(eventData, Is.Not.Null);
            Assert.AreEqual(expectedEventId, eventData!.EventId);
            Assert.AreEqual("RequestFailed", eventData.EventName);
            Assert.AreEqual(EventLevel.Informational, eventData.Level);
            Assert.That(eventData.EventSource, Is.SameAs(_eventSource));
            Assert.AreEqual("/service", eventData.Payload![0]);
            Assert.AreEqual("ice_id", eventData.Payload![1]);
            Assert.AreEqual("IceRpc.RemoteException", eventData.Payload![2]);
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
