// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.ClientServer
{
    [Timeout(30000)]
    public sealed class TracingTests
    {
        [Test]
        public async Task Tracing_InvocationActivityAsync()
        {
            await using var server = new Server
            {
                Endpoint = TestHelper.GetUniqueColocEndpoint(),
                Dispatcher = new GreeterService()
            };
            server.Listen();

            {
                await using var connection = new Connection { RemoteEndpoint = server.ProxyEndpoint };
                var pipeline = new Pipeline();
                // The invocation activity is only created if the logger is enabled or Activity.Current is set.
                var prx = IGreeterTestServicePrx.FromConnection(connection);
                Activity? invocationActivity = null;
                bool called = false;
                pipeline.Use(Interceptors.Tracer);
                pipeline.Use(next => new InlineInvoker((request, cancel) =>
                {
                    called = true;
                    invocationActivity = Activity.Current;
                    return next.InvokeAsync(request, cancel);
                }));
                prx.Invoker = pipeline;
                await prx.IcePingAsync();
                Assert.IsTrue(called);
                Assert.IsNull(invocationActivity);
            }

            {
                // Starting the test activity ensures that Activity.Current is not null which in turn will
                // trigger the creation of the Invocation activity.
                Activity testActivity = new Activity("TestActivity");
                testActivity.Start();
                Assert.IsNotNull(Activity.Current);

                Activity? invocationActivity = null;
                await using var connection = new Connection { RemoteEndpoint = server.ProxyEndpoint };
                var pipeline = new Pipeline();
                var prx = IGreeterTestServicePrx.FromConnection(connection);
                prx.Invoker = pipeline;
                pipeline.Use(Interceptors.Tracer);
                pipeline.Use(next => new InlineInvoker((request, cancel) =>
                {
                    invocationActivity = Activity.Current;
                    return next.InvokeAsync(request, cancel);
                }));
                await prx.IcePingAsync();
                Assert.IsNotNull(invocationActivity);
                Assert.AreEqual("/IceRpc.Tests.ClientServer.GreeterTestService/ice_ping", invocationActivity.DisplayName);
                Assert.AreEqual(testActivity, invocationActivity.Parent);
                Assert.AreEqual(testActivity, Activity.Current);
                testActivity.Stop();
            }
        }

        [Test]
        public async Task Tracing_DispatchActivityAsync()
        {
            await using var pool = new ConnectionPool();

            var router = new Router();
            Activity? dispatchActivity = null;
            bool called = false;
            router.Use(Middleware.Tracer);
            router.Use(next => new InlineDispatcher(
                async (current, cancel) =>
                {
                    called = true;
                    dispatchActivity = Activity.Current;
                    return await next.DispatchAsync(current, cancel);
                }));
            router.Map("/", new GreeterService());

            await using var server1 = new Server
            {
                Invoker = pool,
                Endpoint = TestHelper.GetUniqueColocEndpoint(),
                Dispatcher = router
            };
            server1.Listen();

            // The dispatch activity is only created if the logger is enabled, Activity.Current is set or
            // the server has an ActivitySource with listeners.
            var prx = IGreeterTestServicePrx.FromServer(server1, "/");
            await prx.IcePingAsync();
            Assert.IsTrue(called);
            Assert.IsNull(dispatchActivity);
            await server1.ShutdownAsync();

            using var activitySource = new ActivitySource("TracingTestActivitySource");

            // Add a listener to ensure the ActivitySource creates a non null activity for the dispatch
            var dispatchStartedActivities = new List<Activity>();
            var dispatchStoppedActivities = new List<Activity>();
            var waitForStopSemaphore = new SemaphoreSlim(0);
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

            router = new Router();
            // Now configure the CustomTracer with an ActivitySource to trigger the creation of the Dispatch activity.
            router.Use(Middleware.CustomTracer(new Middleware.TracerOptions
            {
                ActivitySource = activitySource
            }));
            router.Use(next => new InlineDispatcher(
                async (current, cancel) =>
                {
                    called = true;
                    dispatchActivity = Activity.Current;
                    return await next.DispatchAsync(current, cancel);
                }));
            router.Map("/", new GreeterService());


            await using var server2 = new Server
            {
                Invoker = pool,
                Endpoint = TestHelper.GetUniqueColocEndpoint(),
                Dispatcher = router,
                ActivitySource = activitySource
            };

            server2.Listen();
            prx = IGreeterTestServicePrx.FromServer(server2, "/");
            await prx.IcePingAsync();
            // Await the server shutdown to ensure the dispatch has finish
            await server2.ShutdownAsync();
            Assert.IsNotNull(dispatchActivity);
            Assert.AreEqual("//ice_ping", dispatchActivity.DisplayName);
            // Wait to receive the dispatch activity stop event
            Assert.That(await waitForStopSemaphore.WaitAsync(TimeSpan.FromSeconds(30)), Is.True);
            CollectionAssert.AreEqual(dispatchStartedActivities, dispatchStoppedActivities);
        }

        /// <summary>Ensure that the Invocation activity is restored in the server side and used as the
        /// parent activity for the Dispatch activity. This test runs with the two supported protocols because
        /// the propagation of the activity context is different for Ice2 and Ice1.</summary>
        [TestCase(Protocol.Ice1)]
        [TestCase(Protocol.Ice2)]
        public async Task Tracing_ActivityPropagationAsync(Protocol protocol)
        {
            await using var pool = new ConnectionPool();

            Activity? invocationActivity = null;
            Activity? dispatchActivity = null;

            using var activitySource = new ActivitySource("TracingTestActivitySource");
            // Add a listener to ensure the ActivitySource creates a non null activity for the dispatch
            var waitForStopSemaphore = new SemaphoreSlim(0);
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
            router.Use(Middleware.CustomTracer(new Middleware.TracerOptions
            {
                ActivitySource = activitySource
            }));
            router.Use(next => new InlineDispatcher(
                async (current, cancel) =>
                {
                    dispatchActivity = Activity.Current;
                    return await next.DispatchAsync(current, cancel);
                }));
            router.Map("/test", new GreeterService());

            await using var server = new Server
            {
                Invoker = pool,
                Endpoint = TestHelper.GetTestEndpoint(protocol: protocol),
                Dispatcher = router,
                ActivitySource = activitySource
            };

            server.Listen();

            var prx = IGreeterTestServicePrx.FromServer(server, "/test");

            // Starting the test activity ensures that Activity.Current is not null which in turn will
            // trigger the creation of the Invocation activity.
            var testActivity = new Activity("TestActivity");
            testActivity.Start();
            Assert.IsNotNull(Activity.Current);

            pool.Use(Interceptors.Tracer);
            pool.Use(next => new InlineInvoker((request, cancel) =>
            {
                invocationActivity = Activity.Current;
                // Add some entries to the baggage to ensure that it is correctly propagated
                // to the server activity.
                invocationActivity?.AddBaggage("Foo", "Bar");
                invocationActivity?.AddBaggage("Foo", "Baz");
                invocationActivity?.AddBaggage("TraceLevel", "Information");
                return next.InvokeAsync(request, cancel);
            }));
            await prx.IcePingAsync();
            // Await the server shutdown to ensure the dispatch has finish
            await server.ShutdownAsync();
            Assert.IsNotNull(invocationActivity);
            Assert.AreEqual(testActivity, invocationActivity.Parent);
            Assert.IsNotNull(dispatchActivity);
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

        public class GreeterService : IGreeterTestService
        {
            public ValueTask SayHelloAsync(Dispatch dispatch, CancellationToken cancel) => default;
        }
    }
}
