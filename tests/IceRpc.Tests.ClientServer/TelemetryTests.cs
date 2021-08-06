// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.ClientServer
{
    [Timeout(30000)]
    public sealed class TelemetryTests
    {
        [Test]
        public async Task Telemetry_InvocationActivityAsync()
        {
            await using var server = new Server
            {
                Endpoint = TestHelper.GetUniqueColocEndpoint(),
                Dispatcher = new Greeter()
            };
            server.Listen();

            {
                await using var connection = new Connection { RemoteEndpoint = server.Endpoint };
                var pipeline = new Pipeline();
                // The invocation activity is only created if the logger is enabled or Activity.Current is set.
                var prx = GreeterPrx.FromConnection(connection);
                Activity? invocationActivity = null;
                bool called = false;
                pipeline.UseTelemetry(new TelemetryOptions());
                pipeline.Use(next => new InlineInvoker((request, cancel) =>
                {
                    called = true;
                    invocationActivity = Activity.Current;
                    return next.InvokeAsync(request, cancel);
                }));
                prx.Proxy.Invoker = pipeline;
                await prx.IcePingAsync();
                Assert.That(called, Is.True);
                Assert.That(invocationActivity, Is.Null);
            }

            {
                // Starting the test activity ensures that Activity.Current is not null which in turn will
                // trigger the creation of the Invocation activity.
                using var testActivity = new Activity("TestActivity");
                testActivity.Start();
                Assert.That(Activity.Current, Is.Not.Null);

                Activity? invocationActivity = null;
                await using var connection = new Connection { RemoteEndpoint = server.Endpoint };
                var pipeline = new Pipeline();
                var prx = GreeterPrx.FromConnection(connection);
                prx.Proxy.Invoker = pipeline;
                pipeline.UseTelemetry(new TelemetryOptions());
                pipeline.Use(next => new InlineInvoker((request, cancel) =>
                {
                    invocationActivity = Activity.Current;
                    return next.InvokeAsync(request, cancel);
                }));
                await prx.IcePingAsync();
                Assert.That(invocationActivity, Is.Not.Null);
                Assert.AreEqual("/IceRpc.Tests.ClientServer.Greeter/ice_ping", invocationActivity.DisplayName);
                Assert.AreEqual(testActivity, invocationActivity.Parent);
                Assert.AreEqual(testActivity, Activity.Current);
                testActivity.Stop();
            }
        }

        [Test]
        public async Task Telemetry_DispatchActivityAsync()
        {
            {
                var router = new Router();
                Activity? dispatchActivity = null;
                bool called = false;
                router.UseTelemetry(new TelemetryMiddleware.Options());
                router.Use(next => new InlineDispatcher(
                    async (current, cancel) =>
                    {
                        called = true;
                        dispatchActivity = Activity.Current;
                        return await next.DispatchAsync(current, cancel);
                    }));
                router.Map<IGreeter>(new Greeter());

                await using var server = new Server
                {
                    Endpoint = TestHelper.GetUniqueColocEndpoint(),
                    Dispatcher = router
                };
                server.Listen();

                // The dispatch activity is only created if the logger is enabled, Activity.Current is set or
                // the server has an ActivitySource with listeners.
                await using var connection = new Connection { RemoteEndpoint = server.Endpoint };
                var prx = GreeterPrx.FromConnection(connection);
                await prx.IcePingAsync();
                Assert.That(called, Is.True);
                Assert.That(dispatchActivity, Is.Null);
                await server.ShutdownAsync();
            }

            {
                Activity? dispatchActivity = null;
                using var activitySource = new ActivitySource("TracingTestActivitySource");

                // Add a listener to ensure the ActivitySource creates a non null activity for the dispatch
                var dispatchStartedActivities = new List<Activity>();
                var dispatchStoppedActivities = new List<Activity>();
                using var waitForStopSemaphore = new SemaphoreSlim(0);
                using var listener = new ActivityListener
                {
                    ShouldListenTo = source => source == activitySource,
                    Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                    ActivityStarted = activity => dispatchStartedActivities.Add(activity),
                    ActivityStopped = activity =>
                    {
                        dispatchStoppedActivities.Add(activity);
                        waitForStopSemaphore.Release();
                    }
                };
                ActivitySource.AddActivityListener(listener);

                var router = new Router();
                // Now configure the CustomTracer with an ActivitySource to trigger the creation of the Dispatch activity.
                router.UseTelemetry(new TelemetryMiddleware.Options { ActivitySource = activitySource });
                router.Use(next => new InlineDispatcher(
                    async (current, cancel) =>
                    {
                        dispatchActivity = Activity.Current;
                        return await next.DispatchAsync(current, cancel);
                    }));
                router.Map<IGreeter>(new Greeter());

                await using var server = new Server
                {
                    Endpoint = TestHelper.GetUniqueColocEndpoint(),
                    Dispatcher = router,
                };
                server.Listen();
                await using var connection = new Connection { RemoteEndpoint = server.Endpoint };
                var prx = GreeterPrx.FromConnection(connection);
                await prx.IcePingAsync();
                // Await the server shutdown to ensure the dispatch has finish
                await server.ShutdownAsync();
                Assert.That(dispatchActivity, Is.Not.Null);
                Assert.AreEqual("/IceRpc.Tests.ClientServer.Greeter/ice_ping",
                                dispatchActivity.DisplayName);
                // Wait to receive the dispatch activity stop event
                Assert.That(await waitForStopSemaphore.WaitAsync(TimeSpan.FromSeconds(30)), Is.True);
                CollectionAssert.AreEqual(dispatchStartedActivities, dispatchStoppedActivities);
            }
        }

        /// <summary>Ensure that the Invocation activity is restored in the server side and used as the
        /// parent activity for the Dispatch activity. This test runs with the two supported protocols because
        /// the propagation of the activity context is different for Ice2 and Ice1.</summary>
        [Test]
        public async Task Telemetry_ActivityPropagationAsync()
        {
            Activity? invocationActivity = null;
            Activity? dispatchActivity = null;

            using var activitySource = new ActivitySource("TracingTestActivitySource");
            // Add a listener to ensure the ActivitySource creates a non null activity for the dispatch
            using var waitForStopSemaphore = new SemaphoreSlim(0);
            var dispatchStartedActivities = new List<Activity>();
            var dispatchStoppedActivities = new List<Activity>();
            using var listener = new ActivityListener
            {
                ShouldListenTo = source => source == activitySource,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                ActivityStarted = activity => dispatchStartedActivities.Add(activity),
                ActivityStopped = activity =>
                    {
                        dispatchStoppedActivities.Add(activity);
                        waitForStopSemaphore.Release();
                    }
            };
            ActivitySource.AddActivityListener(listener);

            var router = new Router();
            router.UseTelemetry(new TelemetryMiddleware.Options { ActivitySource = activitySource });
            router.Use(next => new InlineDispatcher(
                async (current, cancel) =>
                {
                    dispatchActivity = Activity.Current;
                    return await next.DispatchAsync(current, cancel);
                }));
            router.Map<IGreeter>(new Greeter());

            await using var server = new Server
            {
                Endpoint = TestHelper.GetTestEndpoint(),
                Dispatcher = router
            };

            server.Listen();

            await using var connection = new Connection { RemoteEndpoint = server.Endpoint };
            var prx = GreeterPrx.FromConnection(connection);

            // Starting the test activity ensures that Activity.Current is not null which in turn will
            // trigger the creation of the Invocation activity.
            using var testActivity = new Activity("TestActivity");
            testActivity.Start();
            Assert.That(Activity.Current, Is.Not.Null);

            var pipeline = new Pipeline();
            pipeline.UseTelemetry(new TelemetryOptions());
            pipeline.Use(next => new InlineInvoker((request, cancel) =>
            {
                invocationActivity = Activity.Current;
                // Add some entries to the baggage to ensure that it is correctly propagated
                // to the server activity.
                invocationActivity?.AddBaggage("Foo", "Bar");
                invocationActivity?.AddBaggage("Foo", "Baz");
                invocationActivity?.AddBaggage("TraceLevel", "Information");
                return next.InvokeAsync(request, cancel);
            }));
            prx.Proxy.Invoker = pipeline;
            await prx.IcePingAsync();
            // Await the server shutdown to ensure the dispatch has finish
            await server.ShutdownAsync();
            Assert.That(invocationActivity, Is.Not.Null);
            Assert.AreEqual(testActivity, invocationActivity.Parent);
            Assert.That(dispatchActivity, Is.Not.Null);
            Assert.AreEqual(invocationActivity.Id, dispatchActivity.ParentId);

            CollectionAssert.AreEqual(
                invocationActivity.Baggage,
                dispatchActivity.Baggage);

            Assert.AreEqual("Baz", dispatchActivity.GetBaggageItem("Foo"));
            Assert.AreEqual("Information", dispatchActivity.GetBaggageItem("TraceLevel"));
            // Wait to receive the dispatch activity stop event
            Assert.That(await waitForStopSemaphore.WaitAsync(TimeSpan.FromSeconds(30)), Is.True);
            CollectionAssert.AreEqual(dispatchStartedActivities, dispatchStoppedActivities);
        }

        public class Greeter : Service, IGreeter
        {
            public ValueTask SayHelloAsync(Dispatch dispatch, CancellationToken cancel) => default;
        }
    }
}
