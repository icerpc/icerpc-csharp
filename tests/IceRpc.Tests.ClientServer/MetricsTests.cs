// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using NUnit.Framework;
using System.Diagnostics.Tracing;

namespace IceRpc.Tests.ClientServer
{
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    public class MetricsTests
    {
        [Test]
        public async Task Metrics_RequestsAsync()
        {
            using var invocationEventListener = new TestEventListener(
                "IceRpc.Invocation.Test",
                new List<(string, string)>
                {
                    ("total-requests", "10"),
                    ("current-requests", "10"),
                });

            using var dispatchEventListener = new TestEventListener(
                "IceRpc.Dispatch.Test",
                new List<(string, string)>
                {
                    ("total-requests", "10"),
                    ("current-requests", "10"),
                });

            var pipeline = new Pipeline();
            using var invocationEventSource = new InvocationEventSource("IceRpc.Invocation.Test");
            pipeline.UseMetrics(invocationEventSource);

            using var dispatchEventSource = new DispatchEventSource("IceRpc.Dispatch.Test");
            var router = new Router();
            router.UseMetrics(dispatchEventSource);
            int dispatchRequests = 0;
            var mutex = new object();
            using var dispatchSemaphore = new SemaphoreSlim(0);
            router.Use(next => new InlineDispatcher(
                async (request, cancel) =>
                {
                    // Hold the dispatch until we received 10 requests to ensure current-request grows to 10
                    Task? t = null;
                    lock (mutex)
                    {
                        if (++dispatchRequests < 10)
                        {
                            t = dispatchSemaphore.WaitAsync(cancel);
                        }
                        else
                        {
                            dispatchSemaphore.Release(dispatchRequests);
                        }
                    }
                    await (t ?? Task.CompletedTask);
                    // This delay ensure the metrics would be refresh while current-requests is still 10
                    await Task.Delay(TimeSpan.FromSeconds(1), cancel);
                    return await next.DispatchAsync(request, cancel);
                }));
            router.Map<IGreeter>(new Greeter1());
            await using var server = new Server
            {
                Dispatcher = router,
                Endpoint = "ice+coloc://event_source"
            };
            server.Listen();

            await using var connection = new Connection { RemoteEndpoint = server.Endpoint };
            var greeter = GreeterPrx.FromConnection(connection, invoker: pipeline);

            var tasks = new List<Task>();
            for (int i = 0; i < 10; ++i)
            {
                tasks.Add(greeter.SayHelloAsync());
            }

            await Task.WhenAll(tasks);

            Assert.DoesNotThrowAsync(async () => await dispatchEventListener.WaitForCounterEventsAsync());
            Assert.DoesNotThrowAsync(async () => await invocationEventListener.WaitForCounterEventsAsync());
        }

        [Test]
        public async Task Metrics_RequestsCanceledAsync()
        {
            using var invocationEventListener = new TestEventListener(
                "IceRpc.Invocation.Test",
                new List<(string, string)>
                {
                    ("total-requests", "10"),
                    ("canceled-requests", "10"),
                });

            using var dispatchEventListener = new TestEventListener(
                "IceRpc.Dispatch.Test",
                new List<(string, string)>
                {
                    ("total-requests", "10"),
                    ("canceled-requests", "10")
                });

            var pipeline = new Pipeline();
            using var invocationEventSource = new InvocationEventSource("IceRpc.Invocation.Test");
            pipeline.UseMetrics(invocationEventSource);
            using var dispatchEventSource = new DispatchEventSource("IceRpc.Dispatch.Test");
            var router = new Router();
            router.UseMetrics(dispatchEventSource);
            router.Map<IGreeter>(new Greeter2());
            await using var server = new Server
            {
                Dispatcher = router,
                Endpoint = "ice+coloc://event_source"
            };
            server.Listen();

            await using var connection = new Connection { RemoteEndpoint = server.Endpoint };
            var greeter = GreeterPrx.FromConnection(connection, invoker: pipeline);

            var tasks = new List<Task>();
            for (int i = 0; i < 10; ++i)
            {
                tasks.Add(greeter.SayHelloAsync(new Invocation { Timeout = TimeSpan.FromSeconds(1) }));
            }

            Assert.ThrowsAsync<OperationCanceledException>(async () => await Task.WhenAll(tasks));

            Assert.DoesNotThrowAsync(async () => await dispatchEventListener.WaitForCounterEventsAsync());
            Assert.DoesNotThrowAsync(async () => await invocationEventListener.WaitForCounterEventsAsync());
        }

        [Test]
        public async Task Metrics_RequestsFailedAsync()
        {
            using var invocationEventListener = new TestEventListener(
               "IceRpc.Invocation.Test",
               new List<(string, string)>
               {
                    ("total-requests", "10"),
                    ("failed-requests", "10")
               });

            using var dispatchEventListener = new TestEventListener(
                "IceRpc.Dispatch.Test",
                new List<(string, string)>
                {
                    ("total-requests", "10"),
                    ("failed-requests", "10")
                });

            var pipeline = new Pipeline();
            using var invocationEventSource = new InvocationEventSource("IceRpc.Invocation.Test");
            pipeline.UseMetrics(invocationEventSource);
            using var dispatchEventSource = new DispatchEventSource("IceRpc.Dispatch.Test");
            var router = new Router();
            router.UseMetrics(dispatchEventSource);
            router.Map<IGreeter>(new Greeter3());
            await using var server = new Server
            {
                Dispatcher = router,
                Endpoint = "ice+coloc://event_source"
            };
            server.Listen();

            await using var connection = new Connection { RemoteEndpoint = server.Endpoint };
            var greeter = GreeterPrx.FromConnection(connection, invoker: pipeline);

            for (int i = 0; i < 10; ++i)
            {
                Assert.ThrowsAsync<DispatchException>(async () => await greeter.SayHelloAsync());
            }

            Assert.DoesNotThrowAsync(async () => await dispatchEventListener.WaitForCounterEventsAsync());
            Assert.DoesNotThrowAsync(async () => await invocationEventListener.WaitForCounterEventsAsync());
        }

        private class Greeter1 : Service, IGreeter
        {
            public ValueTask SayHelloAsync(Dispatch dispatch, CancellationToken cancel) => default;
        }

        private class Greeter2 : Service, IGreeter
        {
            public async ValueTask SayHelloAsync(Dispatch dispatch, CancellationToken cancel) =>
                await Task.Delay(TimeSpan.FromSeconds(10), cancel);
        }

        private class Greeter3 : Service, IGreeter
        {
            public ValueTask SayHelloAsync(Dispatch dispatch, CancellationToken cancel) =>
                throw new DispatchException("failed");
        }

        private class TestEventListener : EventListener
        {
            public EventSource? EventSource { get; set; }
            public List<(string Key, string Value)> ExpectedEventCounters { get; }
            public List<(string Key, string Value)> ReceivedEventCounters { get; } = new();
            private readonly string _sourceName;
            private readonly SemaphoreSlim _semaphore;
            private readonly object _mutex = new(); // protects ReceivedEventCounters

            public TestEventListener(string sourceName, List<(string Key, string Value)> expectedCounters)
            {
                ExpectedEventCounters = expectedCounters;
                _semaphore = new SemaphoreSlim(0);
                _sourceName = sourceName;
                EventSource? eventSource = EventSource.GetSources().FirstOrDefault(
                    source => source.Name == _sourceName);
                if (eventSource != null)
                {
                    EnableEvents(eventSource);
                }
            }

            public async Task WaitForCounterEventsAsync()
            {
                for (int i = 0; i < ExpectedEventCounters.Count; ++i)
                {
                    if (!await _semaphore.WaitAsync(TimeSpan.FromSeconds(30)))
                    {
                        break;
                    }
                }
                lock (_mutex)
                {
                    CollectionAssert.AreEquivalent(ExpectedEventCounters, ReceivedEventCounters);
                }
            }

            protected override void OnEventSourceCreated(EventSource eventSource)
            {
                // OnEventSourceCreated can be called as soon as the base constructor runs and before
                // _sourceName is assigned, if that is the case we ignore the source.
                if (_sourceName == null)
                {
                    return;
                }

                if (_sourceName == eventSource.Name)
                {
                    EnableEvents(eventSource);
                }
            }

            protected override void OnEventWritten(EventWrittenEventArgs eventData)
            {
                if (eventData.EventSource == EventSource && eventData.EventId == -1) // counter event
                {
                    Assert.That(eventData.Payload, Is.Not.Null);
                    var eventPayload = (IDictionary<string, object?>)eventData.Payload[0]!;

                    string name = "";
                    if (eventPayload.TryGetValue("Name", out object? nameValue))
                    {
                        name = nameValue?.ToString() ?? "";
                    }

                    string value = "";
                    if (eventPayload.TryGetValue("Increment", out object? incrementValue))
                    {
                        value = incrementValue?.ToString() ?? "";
                    }
                    else if (eventPayload.TryGetValue("Mean", out object? meanValue))
                    {
                        value = meanValue?.ToString() ?? "";
                    }

                    foreach ((string Key, string Value) entry in ExpectedEventCounters)
                    {
                        if (entry.Key == name && entry.Value == value && !ReceivedEventCounters.Contains(entry))
                        {
                            lock (_mutex)
                            {
                                ReceivedEventCounters.Add(entry);
                            }
                            _semaphore.Release();
                            break;
                        }
                    }

                }
            }

            private void EnableEvents(EventSource eventSource)
            {
                lock (_mutex)
                {
                    if (EventSource == null)
                    {
                        EventSource = eventSource;
                        EnableEvents(eventSource,
                                     EventLevel.LogAlways,
                                     EventKeywords.All,
                                     new Dictionary<string, string?>
                                     {
                                        { "EventCounterIntervalSec", "0.001" }
                                     });
                    }
                }
            }
        }
    }
}
