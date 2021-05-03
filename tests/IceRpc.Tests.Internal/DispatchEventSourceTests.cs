// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using NUnit.Framework;
using System;
using System.Diagnostics.Tracing;
using System.Threading.Tasks;

namespace IceRpc.Tests.Internal
{
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    public class DispatchEventSourceTests
    {
        private DispatchEventSource _eventSource;

        public DispatchEventSourceTests() =>
            _eventSource = new DispatchEventSource(Guid.NewGuid().ToString());

        [Test]
        public async Task DispatchEventSource_RequestStartAsync()
        {
            var expectedEventId = 1;
            var eventListener = new TestEventListener(expectedEventId);
            eventListener.EnableEvents(_eventSource, EventLevel.Verbose);

            await using var communicator = new Communicator();
            var prx = IServicePrx.Parse("ice+tcp://localhost/service", communicator);

            var outgoingRequest = new OutgoingRequest(prx,
                                                      "ice_id",
                                                      Payload.FromEmptyArgs(prx),
                                                      DateTime.MaxValue);

            var request = new IncomingRequest(outgoingRequest);
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
        public async Task DispatchEventSource_RequestStopAsync()
        {
            var expectedEventId = 2;
            var eventListener = new TestEventListener(expectedEventId);
            eventListener.EnableEvents(_eventSource, EventLevel.Verbose);

            await using var communicator = new Communicator();
            var prx = IServicePrx.Parse("ice+tcp://localhost/service", communicator);
            var outgoingRequest = new OutgoingRequest(prx,
                                                      "ice_id",
                                                      Payload.FromEmptyArgs(prx),
                                                      DateTime.MaxValue);

            var request = new IncomingRequest(outgoingRequest);
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
        public async Task DispatchEventSource_RequestCanceledAsync()
        {
            var expectedEventId = 3;
            var eventListener = new TestEventListener(expectedEventId);
            eventListener.EnableEvents(_eventSource, EventLevel.Verbose);

            await using var communicator = new Communicator();
            var prx = IServicePrx.Parse("ice+tcp://localhost/service", communicator);
            var outgoingRequest = new OutgoingRequest(prx,
                                                      "ice_id",
                                                      Payload.FromEmptyArgs(prx),
                                                      DateTime.MaxValue);

            var request = new IncomingRequest(outgoingRequest);
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
        public async Task DispatchEventSource_RequestFailedAsync()
        {
            var expectedEventId = 4;
            var eventListener = new TestEventListener(expectedEventId);
            eventListener.EnableEvents(_eventSource, EventLevel.Verbose);

            await using var communicator = new Communicator();
            var prx = IServicePrx.Parse("ice+tcp://localhost/service", communicator);
            var outgoingRequest = new OutgoingRequest(prx,
                                                      "ice_id",
                                                      Payload.FromEmptyArgs(prx),
                                                      DateTime.MaxValue);

            var request = new IncomingRequest(outgoingRequest);
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
