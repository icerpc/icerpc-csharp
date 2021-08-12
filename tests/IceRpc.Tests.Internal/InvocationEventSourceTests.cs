// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System.Diagnostics.Tracing;

namespace IceRpc.Tests.Internal
{
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

            var proxy = Proxy.Parse("ice+tcp://localhost/service");
            var request = new OutgoingRequest(proxy,
                                              "ice_id",
                                              Payload.FromEmptyArgs(proxy),
                                              null,
                                              DateTime.MaxValue);
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
        public void InvocationEventSource_RequestStop()
        {
            int expectedEventId = 2;
            using var eventListener = new TestEventListener(expectedEventId);
            eventListener.EnableEvents(_eventSource, EventLevel.Verbose);

            var proxy = Proxy.Parse("ice+tcp://localhost/service");
            var request = new OutgoingRequest(proxy,
                                              "ice_id",
                                              Payload.FromEmptyArgs(proxy),
                                              null,
                                              DateTime.MaxValue);
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
        public void InvocationEventSource_RequestCanceled()
        {
            int expectedEventId = 3;
            using var eventListener = new TestEventListener(expectedEventId);
            eventListener.EnableEvents(_eventSource, EventLevel.Verbose);

            var proxy = Proxy.Parse("ice+tcp://localhost/service");
            var request = new OutgoingRequest(proxy,
                                              "ice_id",
                                              Payload.FromEmptyArgs(proxy),
                                              null,
                                              DateTime.MaxValue);
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
        public void InvocationEventSource_RequestFailed()
        {
            int expectedEventId = 4;
            using var eventListener = new TestEventListener(expectedEventId);
            eventListener.EnableEvents(_eventSource, EventLevel.Verbose);

            var proxy = Proxy.Parse("ice+tcp://localhost/service");
            var request = new OutgoingRequest(proxy,
                                              "ice_id",
                                              Payload.FromEmptyArgs(proxy),
                                              null,
                                              DateTime.MaxValue);
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
