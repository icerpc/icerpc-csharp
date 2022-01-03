// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Diagnostics;

namespace IceRpc.Tests.ClientServer
{
    [Timeout(30000)]
    public sealed class TelemetryTests
    {
        [Test]
        public async Task Telemetry_InvocationActivityAsync()
        {
            Activity? invocationActivity = null;
            bool called = false;

            await using ServiceProvider serviceProvider = new IntegrationTestServiceCollection()
                .AddTransient<IDispatcher, Greeter>()
                .AddTransient<IInvoker>(_ =>
                {
                    var pipeline = new Pipeline();
                    pipeline.UseTelemetry();
                    pipeline.Use(next => new InlineInvoker((request, cancel) =>
                    {
                        called = true;
                        invocationActivity = Activity.Current;
                        return next.InvokeAsync(request, cancel);
                    }));
                    return pipeline;
                })
                .BuildServiceProvider();

            {
                await using AsyncServiceScope scope = serviceProvider.CreateAsyncScope();
                GreeterPrx prx = scope.ServiceProvider.GetProxy<GreeterPrx>();
                await prx.IcePingAsync();
                Assert.That(called, Is.True);
                Assert.That(invocationActivity, Is.Null);
            }

            invocationActivity = null;
            called = false;

            {
                await using AsyncServiceScope scope = serviceProvider.CreateAsyncScope();
                GreeterPrx prx = scope.ServiceProvider.GetProxy<GreeterPrx>();

                // Starting the test activity ensures that Activity.Current is not null which in turn will
                // trigger the creation of the Invocation activity.
                using var testActivity = new Activity("TestActivity");
                testActivity.Start();
                Assert.That(Activity.Current, Is.Not.Null);

                await prx.IcePingAsync();

                Assert.That(called, Is.True);
                Assert.That(invocationActivity, Is.Not.Null);
                Assert.AreEqual("/IceRpc.Tests.ClientServer.Greeter/ice_ping", invocationActivity!.DisplayName);
                Assert.AreEqual(testActivity, invocationActivity.Parent);
                Assert.AreEqual(testActivity, Activity.Current);
                testActivity.Stop();
            }
        }

        [Test]
        public async Task Telemetry_DispatchActivityAsync()
        {
            {
                Activity? dispatchActivity = null;
                bool called = false;

                await using ServiceProvider serviceProvider = new IntegrationTestServiceCollection()
                    .AddTransient<IDispatcher>(_ =>
                    {
                        var router = new Router();
                        router.UseTelemetry();
                        router.Use(next => new InlineDispatcher(
                            async (current, cancel) =>
                            {
                                called = true;
                                dispatchActivity = Activity.Current;
                                return await next.DispatchAsync(current, cancel);
                            }));
                        router.Map<IGreeter>(new Greeter());
                        return router;
                    })
                    .BuildServiceProvider();

                await serviceProvider.GetProxy<GreeterPrx>().IcePingAsync();
                Assert.That(called, Is.True);
                Assert.That(dispatchActivity, Is.Null);
            }

            {
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

                Activity? dispatchActivity = null;
                bool called = false;

                await using ServiceProvider serviceProvider = new IntegrationTestServiceCollection()
                    .AddTransient<IDispatcher>(_ =>
                    {
                        var router = new Router();
                        router.UseTelemetry(new TelemetryOptions { ActivitySource = activitySource });
                        router.Use(next => new InlineDispatcher(
                            async (current, cancel) =>
                            {
                                called = true;
                                dispatchActivity = Activity.Current;
                                return await next.DispatchAsync(current, cancel);
                            }));
                        router.Map<IGreeter>(new Greeter());
                        return router;
                    })
                    .BuildServiceProvider();

                await serviceProvider.GetProxy<GreeterPrx>().IcePingAsync();
                // Await the server shutdown to ensure the dispatch has finish
                await serviceProvider.GetRequiredService<Server>().ShutdownAsync();
                Assert.That(called, Is.True);
                Assert.That(dispatchActivity, Is.Not.Null);
                Assert.AreEqual("/IceRpc.Tests.ClientServer.Greeter/ice_ping", dispatchActivity!.DisplayName);
                // Wait to receive the dispatch activity stop event
                Assert.That(await waitForStopSemaphore.WaitAsync(TimeSpan.FromSeconds(30)), Is.True);
                CollectionAssert.AreEqual(dispatchStartedActivities, dispatchStoppedActivities);
            }
        }

        /// <summary>Ensure that the Invocation activity is restored in the server side and used as the
        /// parent activity for the Dispatch activity. This test runs with the two supported protocols because
        /// the propagation of the activity context is different for IceRpc and Ice1.</summary>
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

            await using ServiceProvider serviceProvider = new IntegrationTestServiceCollection()
                    .AddTransient<IDispatcher>(_ =>
                    {
                        var router = new Router();
                        router.UseTelemetry(new TelemetryOptions { ActivitySource = activitySource });
                        router.Use(next => new InlineDispatcher(
                            async (current, cancel) =>
                            {
                                dispatchActivity = Activity.Current;
                                return await next.DispatchAsync(current, cancel);
                            }));
                        router.Map<IGreeter>(new Greeter());
                        return router;
                    })
                    .AddTransient<IInvoker>(_ =>
                    {
                        var pipeline = new Pipeline();
                        pipeline.UseTelemetry();
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
                        return pipeline;
                    })
                    .BuildServiceProvider();

            // Starting the test activity ensures that Activity.Current is not null which in turn will
            // trigger the creation of the Invocation activity.
            using var testActivity = new Activity("TestActivity");
            testActivity.Start();
            Assert.That(Activity.Current, Is.Not.Null);

            await serviceProvider.GetProxy<GreeterPrx>().IcePingAsync();
            // Await the server shutdown to ensure the dispatch has finish
            await serviceProvider.GetRequiredService<Server>().ShutdownAsync();
            Assert.That(invocationActivity, Is.Not.Null);
            Assert.AreEqual(testActivity, invocationActivity!.Parent);
            Assert.That(dispatchActivity, Is.Not.Null);
            Assert.AreEqual(invocationActivity.Id, dispatchActivity!.ParentId);

            CollectionAssert.AreEqual(invocationActivity.Baggage, dispatchActivity.Baggage);

            Assert.AreEqual("Baz", dispatchActivity.GetBaggageItem("Foo"));
            Assert.AreEqual("Information", dispatchActivity.GetBaggageItem("TraceLevel"));
            // Wait to receive the dispatch activity stop event
            Assert.That(await waitForStopSemaphore.WaitAsync(TimeSpan.FromSeconds(30)), Is.True);
            CollectionAssert.AreEqual(dispatchStartedActivities, dispatchStoppedActivities);
        }

        public class Greeter : Service, IGreeter
        {
            public ValueTask SayHelloAsync(string message, Dispatch dispatch, CancellationToken cancel) => default;
        }
    }
}
