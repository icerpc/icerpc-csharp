// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Diagnostics.Tracing;

namespace IceRpc.Tests.ClientServer
{
    [Timeout(5000)]
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

            using var dispatchEventSource = new DispatchEventSource("IceRpc.Dispatch.Test");
            int dispatchRequests = 0;
            object mutex = new();
            using var dispatchSemaphore = new SemaphoreSlim(0);
            using var invocationEventSource = new InvocationEventSource("IceRpc.Invocation.Test");

            await using ServiceProvider serviceProvider = new IntegrationTestServiceCollection()
                .AddTransient<IDispatcher>(_ =>
                {
                    var router = new Router();
                    router.UseMetrics(dispatchEventSource);
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
                            await Task.Delay(TimeSpan.FromSeconds(2), cancel);
                            return await next.DispatchAsync(request, cancel);
                        }));
                    router.Map<IGreeter>(new Greeter1());
                    return router;
                })
                .AddTransient<IInvoker>(_ => new Pipeline().UseMetrics(invocationEventSource))
                .BuildServiceProvider();

            GreeterPrx greeter = serviceProvider.GetProxy<GreeterPrx>();

            var tasks = new List<Task>();
            for (int i = 0; i < 10; ++i)
            {
                tasks.Add(greeter.SayHelloAsync("hello"));
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

            using var dispatchEventSource = new DispatchEventSource("IceRpc.Dispatch.Test");
            using var invocationEventSource = new InvocationEventSource("IceRpc.Invocation.Test");

            await using ServiceProvider serviceProvider = new IntegrationTestServiceCollection()
                .AddTransient<IDispatcher>(_ =>
                {
                    var router = new Router();
                    router.UseMetrics(dispatchEventSource);
                    router.Map<IGreeter>(new Greeter2());
                    return router;
                })
                .AddTransient<IInvoker>(_ => new Pipeline().UseMetrics(invocationEventSource))
                .BuildServiceProvider();

            GreeterPrx greeter = serviceProvider.GetProxy<GreeterPrx>();

            var tasks = new List<Task>();
            for (int i = 0; i < 10; ++i)
            {
                tasks.Add(greeter.SayHelloAsync("hello", new Invocation { Timeout = TimeSpan.FromSeconds(2) }));
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

            using var invocationEventSource = new InvocationEventSource("IceRpc.Invocation.Test");
            using var dispatchEventSource = new DispatchEventSource("IceRpc.Dispatch.Test");

            await using ServiceProvider serviceProvider = new IntegrationTestServiceCollection()
                .AddTransient<IDispatcher>(_ =>
                {
                    var router = new Router();
                    router.UseMetrics(dispatchEventSource);
                    router.Map<IGreeter>(new Greeter3());
                    return router;
                })
                .AddTransient<IInvoker>(_ => new Pipeline().UseMetrics(invocationEventSource))
                .BuildServiceProvider();

            GreeterPrx greeter = serviceProvider.GetProxy<GreeterPrx>();

            for (int i = 0; i < 10; ++i)
            {
                Assert.ThrowsAsync<DispatchException>(async () => await greeter.SayHelloAsync("hello"));
            }

            Assert.DoesNotThrowAsync(async () => await dispatchEventListener.WaitForCounterEventsAsync());
            Assert.DoesNotThrowAsync(async () => await invocationEventListener.WaitForCounterEventsAsync());
        }

        private class Greeter1 : Service, IGreeter
        {
            public ValueTask SayHelloAsync(string message, Dispatch dispatch, CancellationToken cancel) => default;
        }

        private class Greeter2 : Service, IGreeter
        {
            public async ValueTask SayHelloAsync(string message, Dispatch dispatch, CancellationToken cancel) =>
                await Task.Delay(TimeSpan.FromSeconds(10), cancel);
        }

        private class Greeter3 : Service, IGreeter
        {
            public ValueTask SayHelloAsync(string message, Dispatch dispatch, CancellationToken cancel) =>
                throw new DispatchException("failed");
        }

        private class TestEventListener : EventListener
        {
            public List<(string Key, string Value)> ExpectedEventCounters { get; }
            public List<(string Key, string Value)> ReceivedEventCounters { get; } = new();
            private EventSource? _eventSource;
            private readonly string _sourceName;
            private readonly SemaphoreSlim _semaphore;
            private readonly object _mutex = new(); // protects ReceivedEventCounters

            public TestEventListener(string sourceName, List<(string Key, string Value)> expectedCounters)
            {
                ExpectedEventCounters = expectedCounters;
                _semaphore = new SemaphoreSlim(0);
                _sourceName = sourceName;
            }

            public async Task WaitForCounterEventsAsync()
            {
                for (int i = 0; i < ExpectedEventCounters.Count; ++i)
                {
                    await _semaphore.WaitAsync();
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
                    lock (_mutex)
                    {
                        if (_eventSource == null)
                        {
                            _eventSource = eventSource;
                            EnableEvents(eventSource,
                                         EventLevel.LogAlways,
                                         EventKeywords.All,
                                         new Dictionary<string, string?>
                                         {
                                            { "EventCounterIntervalSec", "1" }
                                         });
                        }
                    }
                }
            }

            protected override void OnEventWritten(EventWrittenEventArgs eventData)
            {
                if (eventData.EventSource == _eventSource && eventData.EventId == -1) // counter event
                {
                    Assert.That(eventData.Payload, Is.Not.Null);
                    var eventPayload = (IDictionary<string, object?>)eventData.Payload![0]!;

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
        }
    }
}
