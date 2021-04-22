// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.ClientServer
{
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

            // The invocation activity is only created if the logger is enabled or Activity.Current
            // is set.
            var prx = server.CreateProxy<IGreeterTestServicePrx>("/");
            Activity? current = null;
            bool called = false;
            prx.InvocationInterceptors = ImmutableList.Create<InvocationInterceptor>(
                    async (target, request, next, cancel) =>
                    {
                        called = true;
                        current = Activity.Current;
                        return await next(target, request, cancel);
                    });
            await prx.IcePingAsync();
            Assert.IsTrue(called);
            Assert.IsNull(current);

            Activity testActivity = new Activity("TestActivity");
            testActivity.Start();

            prx.InvocationInterceptors = ImmutableList.Create<InvocationInterceptor>(
                    async (target, request, next, cancel) =>
                    {
                        called = true;
                        current = Activity.Current;
                        return await next(target, request, cancel);
                    });
            await prx.IcePingAsync();
            Assert.IsTrue(called);
            Assert.IsNotNull(current);
            Assert.AreEqual("IceRpc.Invocation", current.DisplayName);
            Assert.AreEqual(testActivity, current.Parent);
            Assert.AreEqual(testActivity, Activity.Current);
            testActivity.Stop();

            testActivity = new Activity("TestActivity");
            testActivity.Start();
            prx.InvocationInterceptors = ImmutableList.Create<InvocationInterceptor>(
                    async (target, request, next, cancel) =>
                    {
                        called = true;
                        current = Activity.Current;
                        var interceptedActivity = new Activity("Intercepted");
                        try
                        {
                            interceptedActivity.Start();
                            return await next(target, request, cancel);
                        }
                        finally
                        {
                            interceptedActivity.Stop();
                        }
                    });
            await prx.IcePingAsync();
            Assert.IsTrue(called);
            Assert.IsNotNull(current);
            Assert.AreEqual("IceRpc.Invocation", current.DisplayName);
            Assert.AreEqual(testActivity, current.Parent);
            Assert.AreEqual(testActivity, Activity.Current);
            testActivity.Stop();
        }

        [Test]
        public async Task Tracing_DispatchActivityAsync()
        {
            await using var communicator = new Communicator();

            var router = new Router();
            Activity? currentActivity = null;
            bool called = false;
            router.Use(next => new InlineDispatcher(
                async (current, cancel) =>
                {
                    called = true;
                    currentActivity = Activity.Current;
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

            // The dispatch activity is only created if the logger is enabled or Activity.Current
            // is set.
            var prx = server1.CreateProxy<IGreeterTestServicePrx>("/");
            await prx.IcePingAsync();
            Assert.IsTrue(called);
            Assert.IsNull(currentActivity);
            await server1.ShutdownAsync();
          
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
            called = false;
            await prx.IcePingAsync();
            Assert.IsTrue(called);
            Assert.IsNotNull(currentActivity);
            Assert.AreEqual("IceRpc.Dispatch", currentActivity.DisplayName);
        }

        /// <summary>Ensure that the Invocation activity is restored in the server side and used as the
        /// parent activity for the Dispatch activity.</summary>
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

            await using var server = new Server
            {
                Communicator = communicator,
                Endpoint = TestHelper.GetTestEndpoint(protocol: protocol),
                Dispatcher = router,
                ActivitySource = new ActivitySource("TracingTestActivitySource")
            };

            // Add a listener to ensure the ActivitySource creates a non null activity
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
        }

        public class GreeterService : IAsyncGreeterTestService
        {
            public ValueTask SayHelloAsync(Current current, CancellationToken cancel) => default;
        }
    }
}