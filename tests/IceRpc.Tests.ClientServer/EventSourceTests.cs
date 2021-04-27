// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.ClientServer
{
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    public class EventSourceTests
    {
        [Test]
        public async Task EventSource_RequestsAsync()
        {
            await using var communicator = new Communicator();
            var greeter = IGreeterTestServicePrx.Parse("ice+coloc://event_source/test", communicator);
            await using var server = new Server
            {
                Communicator = communicator,
                DispatchEventSource = new DispatchEventSource("IceRpc.Dispatch.Test"),
                Dispatcher = new Greeter1(),
                Endpoint = "ice+coloc://event_source"
            };

            using var invocationEventListener = new TestEventListener(
                "IceRpc.Invocation",
                new (string, string)[]
                {
                    ("total-requests", "10"),
                    ("current-requests", "10"),
                    ("current-requests", "0"),  // Back to 0 after the request finish
                    ("requests-per-second", "1")
                });

            using var dispatchEventListener = new TestEventListener(
                "IceRpc.Dispatch.Test",
                new (string, string)[]
                {
                    ("total-requests", "10"),
                    ("current-requests", "10"),
                    ("current-requests", "0"),  // Back to 0 after the request finish
                    ("requests-per-second", "1")
                });

            InvocationEventSource.Log.ResetCounters();

            server.Listen();
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
        public async Task EventSource_RequestsCanceledAsync()
        {
            await using var communicator = new Communicator();
            var greeter = IGreeterTestServicePrx.Parse("ice+coloc://event_source/test", communicator);
            await using var server = new Server
            {
                Communicator = communicator,
                DispatchEventSource = new DispatchEventSource("IceRpc.Dispatch.Test"),
                Dispatcher = new Greeter2(),
                Endpoint = "ice+coloc://event_source"
            };

            using var invocationEventListener = new TestEventListener(
                "IceRpc.Invocation",
                new (string, string)[]
                {
                    ("total-requests", "10"),
                    ("current-requests", "10"),
                    ("current-requests", "0"), // Back to 0 after the request finish
                    ("canceled-requests", "10"),
                });

            using var dispatchEventListener = new TestEventListener(
                "IceRpc.Dispatch.Test",
                new (string, string)[]
                {
                    ("total-requests", "10"),
                    ("current-requests", "10"),
                    ("current-requests", "0"), // Back to 0 after the request finish
                    ("canceled-requests", "10")
                });

            InvocationEventSource.Log.ResetCounters();

            server.Listen();
            greeter.InvocationTimeout = TimeSpan.FromSeconds(1);

            var tasks = new List<Task>();
            for (int i = 0; i < 10; ++i)
            {
                tasks.Add(greeter.SayHelloAsync());
            }

            Assert.ThrowsAsync<OperationCanceledException>(async () => await Task.WhenAll(tasks));

            Assert.DoesNotThrowAsync(async () => await dispatchEventListener.WaitForCounterEventsAsync());
            Assert.DoesNotThrowAsync(async () => await invocationEventListener.WaitForCounterEventsAsync());
        }

        [Test]
        public async Task EventSource_RequestsFailedAsync()
        {
            await using var communicator = new Communicator();
            var greeter = IGreeterTestServicePrx.Parse("ice+coloc://event_source/test", communicator);
            await using var server = new Server
            {
                Communicator = communicator,
                DispatchEventSource = new DispatchEventSource("IceRpc.Dispatch.Test"),
                Dispatcher = new Greeter3(),
                Endpoint = "ice+coloc://event_source"
            };

            using var invocationEventListener = new TestEventListener(
                "IceRpc.Invocation",
                new (string, string)[]
                {
                    ("total-requests", "10"),
                    ("current-requests", "1"),
                    ("current-requests", "0"), // Back to 0 after the request finish
                    ("failed-requests", "10")
                });

            using var dispatchEventListener = new TestEventListener(
                "IceRpc.Dispatch.Test",
                new (string, string)[]
                {
                    ("total-requests", "10"),
                    ("current-requests", "1"),
                    ("current-requests", "0"), // Back to 0 after the request finish
                    ("failed-requests", "10")
                });

            InvocationEventSource.Log.ResetCounters();

            server.Listen();
            for (int i = 0; i < 10; ++i)
            {
                Assert.ThrowsAsync<ServerException>(async () => await greeter.SayHelloAsync());
            }

            Assert.DoesNotThrowAsync(async () => await dispatchEventListener.WaitForCounterEventsAsync());
            Assert.DoesNotThrowAsync(async () => await invocationEventListener.WaitForCounterEventsAsync());
        }

        private class Greeter1 : IAsyncGreeterTestService
        {
            public ValueTask SayHelloAsync(Dispatch dispatch, CancellationToken cancel) => default;
        }

        private class Greeter2 : IAsyncGreeterTestService
        {
            public async ValueTask SayHelloAsync(Dispatch dispatch, CancellationToken cancel) =>
                await Task.Delay(TimeSpan.FromSeconds(10), cancel);
        }

        private class Greeter3 : IAsyncGreeterTestService
        {
            public ValueTask SayHelloAsync(Dispatch dispatch, CancellationToken cancel) =>
                throw new ServerException("failed");
        }

        private class TestEventListener : EventListener
        {
            public EventSource? EventSource { get; set; }
            private readonly string _sourceName;
            private readonly List<(string Key, string Value, TaskCompletionSource<object?> Source)> _tasks;

            public TestEventListener(string sourceName, (string Name, string Value)[] expectedCounters)
            {
                _tasks = new List<(string, string, TaskCompletionSource<object?>)>();
                foreach ((string key, string value) in expectedCounters)
                {
                    _tasks.Add((key, value, new TaskCompletionSource<object?>()));
                }
                _sourceName = sourceName;
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
                    EventSource = eventSource;
                    EnableEvents(eventSource,
                                 EventLevel.Verbose,
                                 EventKeywords.All,
                                 new Dictionary<string, string?>
                                 {
                                     { "EventCounterIntervalSec", "0.001" }
                                 });
                }
                base.OnEventSourceCreated(eventSource);
            }

            protected override void OnEventWritten(EventWrittenEventArgs eventData)
            {
                if (eventData.EventId == -1) // counter event
                {
                    Assert.IsNotNull(eventData.Payload);
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

                    foreach (var entry in _tasks)
                    {
                        if (entry.Key == name && entry.Value == value)
                        {
                            entry.Source.TrySetResult(null);
                            break;
                        }
                    }

                }
                base.OnEventWritten(eventData);
            }

            public async Task WaitForCounterEventsAsync()
            {
                foreach ((string key, string value, TaskCompletionSource<object?> source) in _tasks)
                {
                    Task t = await Task.WhenAny(source.Task, Task.Delay(TimeSpan.FromSeconds(2)));
                    if (t != source.Task)
                    {
                        throw new Exception($"Didn't receive the expected event counter {key} with value {value}");
                    }
                }
            }
        }
    }
}
