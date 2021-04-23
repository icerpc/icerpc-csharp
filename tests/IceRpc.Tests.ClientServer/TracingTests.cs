// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System.Collections.Generic;
using System.Collections.Immutable;
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
            await using var communicator = new Communicator();

            await using var server = new Server
            {
                Communicator = communicator,
                Endpoint = TestHelper.GetUniqueColocEndpoint(),
                Dispatcher = new GreeterService()
            };
            server.Listen();

            // The invocation activity is only created if the logger is enabled or Activity.Current is set.
            var prx = server.CreateProxy<IGreeterTestServicePrx>("/");
            Activity? invocationActivity = null;
            bool called = false;
            prx.InvocationInterceptors = ImmutableList.Create<InvocationInterceptor>(
                    async (target, request, next, cancel) =>
                    {
                        called = true;
                        invocationActivity = Activity.Current;
                        return await next(target, request, cancel);
                    });
            await prx.IcePingAsync();
            Assert.IsTrue(called);
            Assert.IsNull(invocationActivity);

            // Starting the test activity ensures that Activity.Current is not null which in turn will
            // trigger the creation of the Invocation activity.
            Activity testActivity = new Activity("TestActivity");
            testActivity.Start();
            Assert.IsNotNull(Activity.Current);

            prx.InvocationInterceptors = ImmutableList.Create<InvocationInterceptor>(
                    async (target, request, next, cancel) =>
                    {
                        invocationActivity = Activity.Current;
                        return await next(target, request, cancel);
                    });
            await prx.IcePingAsync();
            Assert.IsNotNull(invocationActivity);
            Assert.AreEqual("IceRpc.Invocation", invocationActivity.DisplayName);
            Assert.AreEqual(testActivity, invocationActivity.Parent);
            Assert.AreEqual(testActivity, Activity.Current);
            testActivity.Stop();
        }

        [Test]
        public async Task Tracing_DispatchActivityAsync()
        {
            await using var communicator = new Communicator();

            var router = new Router();
            Activity? dispatchActivity = null;
            bool called = false;
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
                Communicator = communicator,
                Endpoint = TestHelper.GetUniqueColocEndpoint(),
                Dispatcher = router
            };
            server1.Listen();

            // The dispatch activity is only created if the logger is enabled, Activity.Current is set or
            // the server has an ActivitySource with listeners.
            var prx = server1.CreateProxy<IGreeterTestServicePrx>("/");
            await prx.IcePingAsync();
            Assert.IsTrue(called);
            Assert.IsNull(dispatchActivity);
            await server1.ShutdownAsync();

            // Now configure the server with an ActivitySource to trigger the creation of the Dispatch activity.
            await using var server2 = new Server
            {
                Communicator = communicator,
                Endpoint = TestHelper.GetUniqueColocEndpoint(),
                Dispatcher = router,
                ActivitySource = new ActivitySource("TracingTestActivitySource")
            };

            using var listener = new ActivityListener
            {
                ShouldListenTo = _ => true,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                ActivityStarted = activity =>
                {
                },
                ActivityStopped = activity =>
                {
                }
            };
            ActivitySource.AddActivityListener(listener);

            server2.Listen();
            prx = server2.CreateProxy<IGreeterTestServicePrx>("/");
            await prx.IcePingAsync();
            Assert.IsNotNull(dispatchActivity);
            Assert.AreEqual("IceRpc.Dispatch", dispatchActivity.DisplayName);
        }

        /// <summary>Ensure that the Invocation activity is restored in the server side and used as the
        /// parent activity for the Dispatch activity. This test runs with the two supported protocols because
        /// the propagation of the activity context is different for Ice2 and Ice1.</summary>
        [TestCase(Protocol.Ice1)]
        [TestCase(Protocol.Ice2)]
        public async Task Tracing_ActivityPropagationAsync(Protocol protocol)
        {
            await using var communicator = new Communicator();

            var router = new Router();
            Activity? invocationActivity = null;
            Activity? dispatchActivity = null;
            router.Use(next => new InlineDispatcher(
                async (current, cancel) =>
                {
                    dispatchActivity = Activity.Current;
                    return await next.DispatchAsync(current, cancel);
                }));
            router.Map("/test", new GreeterService());

            using var activitySource = new ActivitySource("TracingTestActivitySource");
            await using var server = new Server
            {
                Communicator = communicator,
                Endpoint = TestHelper.GetTestEndpoint(protocol: protocol),
                Dispatcher = router,
                ActivitySource = activitySource
            };

            // Add a listener to ensure the ActivitySource creates a non null activity for the dispatch
            var dispatchStartedActivities = new List<Activity>();
            var dispatchStoppedActivities = new List<Activity>();
            using var listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == activitySource.Name,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                ActivityStarted = activity => 
                    dispatchStartedActivities.Add(activity),
                ActivityStopped = activity => dispatchStoppedActivities.Add(activity)
            };
            ActivitySource.AddActivityListener(listener);

            server.Listen();

            var prx = server.CreateProxy<IGreeterTestServicePrx>("/test");

            // Starting the test activity ensures that Activity.Current is not null which in turn will
            // trigger the creation of the Invocation activity.
            var testActivity = new Activity("TestActivity");
            testActivity.Start();
            Assert.IsNotNull(Activity.Current);

            prx.InvocationInterceptors = ImmutableList.Create<InvocationInterceptor>(
                    async (target, request, next, cancel) =>
                    {
                        invocationActivity = Activity.Current;
                        // Add some entries to the baggage to ensure that it is correctly propagated
                        // to the server activity.
                        invocationActivity?.AddBaggage("Foo", "Bar");
                        invocationActivity?.AddBaggage("Foo", "Baz");
                        invocationActivity?.AddBaggage("TraceLevel", "Information");
                        return await next(target, request, cancel);
                    });
            await prx.IcePingAsync();

            Assert.IsNotNull(invocationActivity);
            Assert.AreEqual(testActivity, invocationActivity.Parent);
            Assert.IsNotNull(dispatchActivity);
            Assert.AreEqual(invocationActivity.Id, dispatchActivity.ParentId);

            CollectionAssert.AreEqual(
                invocationActivity.Baggage,
                dispatchActivity.Baggage);

            Assert.AreEqual("Baz", dispatchActivity.GetBaggageItem("Foo"));
            Assert.AreEqual("Information", dispatchActivity.GetBaggageItem("TraceLevel"));
            Assert.AreEqual(1, dispatchStartedActivities.Count);
            CollectionAssert.AreEqual(dispatchStartedActivities, dispatchStoppedActivities);
        }

        public class GreeterService : IAsyncGreeterTestService
        {
            public ValueTask SayHelloAsync(Current current, CancellationToken cancel) => default;
        }
    }
}
