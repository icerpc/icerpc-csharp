// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Tests.Api
{
    [Timeout(5000)]
    [Parallelizable(ParallelScope.All)]
    public sealed class InvocationTimeoutTests
    {
        /// <summary>Ensure that a request fails with OperationCanceledException after the invocation timeout expires.
        /// </summary>
        /// <param name="delay">The time in milliseconds to hold the dispatch to simulate an slow server.</param>
        /// <param name="timeout">The time in milliseconds used as the invocation timeout.</param>
        [TestCase(10000, 1000)]
        public async Task InvocationTimeout_WithInvocation(int delay, int timeout)
        {
            DateTime? dispatchDeadline = null;
            DateTime? invocationDeadline = null;
            await using ServiceProvider serviceProvider = new IntegrationTestServiceCollection()
                .AddTransient<IDispatcher>(_ =>
                {
                    var router = new Router();
                    router.Use(next => new InlineDispatcher(
                        async (current, cancel) =>
                        {
                            dispatchDeadline = current.Deadline;
                            await Task.Delay(TimeSpan.FromMilliseconds(delay), cancel);
                            return await next.DispatchAsync(current, cancel);
                        }));
                    router.Map<IGreeter>(new Greeter());
                    return router;
                })
                .BuildServiceProvider();

            var pipeline = new Pipeline();
            var prx = ServicePrx.FromConnection(serviceProvider.GetRequiredService<Connection>(), invoker: pipeline);

            pipeline.Use(next => new InlineInvoker((request, cancel) =>
            {
                invocationDeadline = request.Deadline;
                return next.InvokeAsync(request, cancel);
            }));

            var invocation = new Invocation { Timeout = TimeSpan.FromMilliseconds(timeout) };

            DateTime expectedDeadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(timeout);
            Assert.CatchAsync<OperationCanceledException>(async () => await prx.IcePingAsync(invocation));
            Assert.That(dispatchDeadline, Is.Not.Null);
            Assert.That(invocationDeadline, Is.Not.Null);
            // Compare the deadlines as milliseconds because the deadline is transferred as a millisecond
            // value and the conversion of the DateTime to the "long" type can result in a dispatch
            // deadline which is different from the invocation deadline.
            Assert.AreEqual(ToMilliSeconds(dispatchDeadline), ToMilliSeconds(invocationDeadline));
            Assert.That(ToMilliSeconds(dispatchDeadline), Is.GreaterThanOrEqualTo(ToMilliSeconds(expectedDeadline)));
        }

        /// <summary>Ensure that a request fails with OperationCanceledException after the invocation timeout expires.
        /// </summary>
        /// <param name="delay">The time in milliseconds to hold the dispatch to simulate an slow server.</param>
        /// <param name="timeout">The time in milliseconds used as the invocation timeout.</param>
        [TestCase(10000, 1000)]
        public async Task InvocationTimeout_WithTimeoutInterceptor(int delay, int timeout)
        {
            DateTime? dispatchDeadline = null;
            DateTime? invocationDeadline = null;
            await using ServiceProvider serviceProvider = new IntegrationTestServiceCollection()
                .AddTransient<IDispatcher>(_ =>
                {
                    var router = new Router();
                    router.Use(next => new InlineDispatcher(
                        async (current, cancel) =>
                        {
                            Assert.That(cancel.CanBeCanceled, Is.True);
                            dispatchDeadline = current.Deadline;
                            await Task.Delay(TimeSpan.FromMilliseconds(delay), cancel);
                            return await next.DispatchAsync(current, cancel);
                        }));
                    router.Map<IGreeter>(new Greeter());
                    return router;
                })
                .BuildServiceProvider();

            // Setting a timeout with an interceptor
            var pipeline = new Pipeline();
            pipeline.UseDefaultTimeout(TimeSpan.FromMilliseconds(timeout));
            pipeline.Use(next => new InlineInvoker((request, cancel) =>
            {
                invocationDeadline = request.Deadline;
                return next.InvokeAsync(request, cancel);
            }));
            var prx = ServicePrx.FromConnection(serviceProvider.GetRequiredService<Connection>(), invoker: pipeline);
            var expectedDeadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(timeout);
            Assert.CatchAsync<OperationCanceledException>(async () => await prx.IcePingAsync());
            Assert.That(dispatchDeadline, Is.Not.Null);
            Assert.That(invocationDeadline, Is.Not.Null);
            // Compare the deadlines as milliseconds because the deadline is transferred as a millisecond
            // value and the conversion of the DateTime to the "long" type can result in a dispatch
            // deadline which is different from the invocation deadline.
            Assert.AreEqual(ToMilliSeconds(dispatchDeadline), ToMilliSeconds(invocationDeadline));
            Assert.That(ToMilliSeconds(dispatchDeadline), Is.GreaterThanOrEqualTo(ToMilliSeconds(expectedDeadline)));
        }

        /// <summary>Ensure that a request fails with OperationCanceledException after the invocation timeout expires.
        /// </summary>
        /// <param name="delay">The time in milliseconds to hold the dispatch to simulate a slow server.</param>
        /// <param name="timeout">The time in milliseconds used as the invocation timeout.</param>
        [TestCase(10000, 1000)]
        public async Task InvocationTimeout_InvocationPrevails(int delay, int timeout)
        {
            DateTime? dispatchDeadline = null;
            DateTime? invocationDeadline = null;

            await using ServiceProvider serviceProvider = new IntegrationTestServiceCollection()
                .AddTransient<IDispatcher>(_ =>
                {
                    var router = new Router();
                    router.Use(next => new InlineDispatcher(
                        async (current, cancel) =>
                        {
                            dispatchDeadline = current.Deadline;
                            await Task.Delay(TimeSpan.FromMilliseconds(delay), cancel);
                            return await next.DispatchAsync(current, cancel);
                        }));
                    router.Map<IGreeter>(new Greeter());
                    return router;
                })
                .BuildServiceProvider();

            var connection = serviceProvider.GetRequiredService<Connection>();

            var pipeline = new Pipeline();
            var prx = ServicePrx.FromConnection(connection, invoker: pipeline);

            // Invocation timeout prevails
            pipeline = new Pipeline();
            pipeline.UseDefaultTimeout(TimeSpan.FromMilliseconds(timeout * 10));
            pipeline.Use(next => new InlineInvoker((request, cancel) =>
            {
                Assert.That(cancel.CanBeCanceled, Is.True);
                invocationDeadline = request.Deadline;
                return next.InvokeAsync(request, cancel);
            }));
            prx = ServicePrx.FromConnection(connection, invoker: pipeline);
            var invocation = new Invocation { Timeout = TimeSpan.FromMilliseconds(timeout) };
            var expectedDeadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(timeout);
            Assert.CatchAsync<OperationCanceledException>(async () => await prx.IcePingAsync(invocation));
            Assert.That(dispatchDeadline, Is.Not.Null);
            Assert.That(invocationDeadline, Is.Not.Null);
            // Compare the deadlines as milliseconds because the deadline is transferred as a millisecond
            // value and the conversion of the DateTime to the "long" type can result in a dispatch
            // deadline which is different from the invocation deadline.
            Assert.AreEqual(ToMilliSeconds(dispatchDeadline), ToMilliSeconds(invocationDeadline));
            Assert.That(ToMilliSeconds(dispatchDeadline), Is.GreaterThanOrEqualTo(ToMilliSeconds(expectedDeadline)));
        }

        private static long ToMilliSeconds(DateTime? deadline) =>
            (long)(deadline!.Value - DateTime.UnixEpoch).TotalMilliseconds;

        private class Greeter : Service, IGreeter
        {
            public ValueTask SayHelloAsync(string message, Dispatch dispatch, CancellationToken cancel) =>
                throw new NotImplementedException();
        }
    }
}
