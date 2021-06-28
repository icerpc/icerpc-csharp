// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Diagnostics.Tracing;

namespace IceRpc.Tests.Internal
{
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    public class InvocationEventSourceTests
    {
        private readonly InvocationEventSource _eventSource;

        public InvocationEventSourceTests() =>
            _eventSource = new InvocationEventSource(Guid.NewGuid().ToString());

        [Test]
        public void InvocationEventSource_RequestStart()
        {
            var expectedEventId = 1;
            var eventListener = new TestEventListener(expectedEventId);
            eventListener.EnableEvents(_eventSource, EventLevel.Verbose);

            var prx = IServicePrx.Parse("ice+tcp://localhost/service");
            var request = new OutgoingRequest(prx,
                                              "ice_id",
                                              Payload.FromEmptyArgs(prx),
                                              null,
                                              DateTime.MaxValue);
            _eventSource.RequestStart(request);

            var eventData = eventListener.EventData;
            Assert.NotNull(eventData);
            Assert.AreEqual(expectedEventId, eventData!.EventId);
            Assert.AreEqual("RequestStart", eventData.EventName);
            Assert.AreEqual(EventLevel.Informational, eventData.Level);
            Assert.That(eventData.EventSource, Is.SameAs(_eventSource));
            Assert.AreEqual("/service", eventData.Payload![0]);
            Assert.AreEqual("ice_id", eventData.Payload![1]);
        }

        [Test]
        public void InvocationEventSource_RequestStop()
        {
            var expectedEventId = 2;
            var eventListener = new TestEventListener(expectedEventId);
            eventListener.EnableEvents(_eventSource, EventLevel.Verbose);

            var prx = IServicePrx.Parse("ice+tcp://localhost/service");
            var request = new OutgoingRequest(prx,
                                              "ice_id",
                                              Payload.FromEmptyArgs(prx),
                                              null,
                                              DateTime.MaxValue);
            _eventSource.RequestStop(request);

            var eventData = eventListener.EventData;
            Assert.NotNull(eventData);
            Assert.AreEqual(expectedEventId, eventData!.EventId);
            Assert.AreEqual("RequestStop", eventData.EventName);
            Assert.AreEqual(EventLevel.Informational, eventData.Level);
            Assert.That(eventData.EventSource, Is.SameAs(_eventSource));
            Assert.AreEqual("/service", eventData.Payload![0]);
            Assert.AreEqual("ice_id", eventData.Payload![1]);
        }

        [Test]
        public void InvocationEventSource_RequestCanceled()
        {
            var expectedEventId = 3;
            var eventListener = new TestEventListener(expectedEventId);
            eventListener.EnableEvents(_eventSource, EventLevel.Verbose);

            var prx = IServicePrx.Parse("ice+tcp://localhost/service");
            var request = new OutgoingRequest(prx,
                                              "ice_id",
                                              Payload.FromEmptyArgs(prx),
                                              null,
                                              DateTime.MaxValue);
            _eventSource.RequestCanceled(request);

            var eventData = eventListener.EventData;
            Assert.NotNull(eventData);
            Assert.AreEqual(expectedEventId, eventData!.EventId);
            Assert.AreEqual("RequestCanceled", eventData.EventName);
            Assert.AreEqual(EventLevel.Informational, eventData.Level);
            Assert.That(eventData.EventSource, Is.SameAs(_eventSource));
            Assert.AreEqual("/service", eventData.Payload![0]);
            Assert.AreEqual("ice_id", eventData.Payload![1]);
        }

        [Test]
        public void InvocationEventSource_RequestFailed()
        {
            var expectedEventId = 4;
            var eventListener = new TestEventListener(expectedEventId);
            eventListener.EnableEvents(_eventSource, EventLevel.Verbose);

            var prx = IServicePrx.Parse("ice+tcp://localhost/service");
            var request = new OutgoingRequest(prx,
                                              "ice_id",
                                              Payload.FromEmptyArgs(prx),
                                              null,
                                              DateTime.MaxValue);
            _eventSource.RequestFailed(request, "IceRpc.RemoteException");

            var eventData = eventListener.EventData;
            Assert.NotNull(eventData);
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
