// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Slice;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Tests.Api
{
    [Timeout(5000)]
    [Parallelizable(ParallelScope.All)]
    public sealed class InvocationTimeoutTests
    {
        /// <summary>Ensures that a request fails with OperationCanceledException after the invocation timeout expires.
        /// </summary>
        /// <param name="delay">The time in milliseconds to hold the dispatch to simulate an slow server.</param>
        /// <param name="timeout">The time in milliseconds used as the invocation timeout.</param>
        /// <param name="protocol">The protocol to use.</param>
        [TestCase(10000, 1000, "icerpc")]
        [TestCase(10000, 1000, "ice")]
        public async Task InvocationTimeout_WithInvocation(int delay, int timeout, string protocol)
        {
            DateTime? dispatchDeadline = null;
            bool invocationHasDeadline = false;
            await using ServiceProvider serviceProvider = new IntegrationTestServiceCollection()
                .UseProtocol(protocol)
                .AddTransient<IDispatcher>(_ =>
                {
                    var router = new Router();
                    router.Use(next => new InlineDispatcher(
                        async (request, cancel) =>
                        {
                            dispatchDeadline = new Dispatch(request).Deadline;
                            await Task.Delay(TimeSpan.FromMilliseconds(delay), cancel);
                            return await next.DispatchAsync(request, cancel);
                        }));
                    router.Map<IGreeter>(new Greeter());
                    return router;
                })
                .BuildServiceProvider();

            var pipeline = new Pipeline();
            var prx = ServicePrx.FromConnection(serviceProvider.GetRequiredService<Connection>(), invoker: pipeline);

            pipeline.Use(next => new InlineInvoker((request, cancel) =>
            {
                invocationHasDeadline = HasDeadline(request);
                return next.InvokeAsync(request, cancel);
            }));

            var invocation = new Invocation { Timeout = TimeSpan.FromMilliseconds(timeout) };

            DateTime expectedDeadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(timeout);
            Assert.CatchAsync<OperationCanceledException>(async () => await prx.IcePingAsync(invocation));
            Assert.That(dispatchDeadline, Is.Not.Null);
            Assert.That(invocationHasDeadline, Is.True);

            double difference = (dispatchDeadline!.Value - expectedDeadline).TotalMilliseconds;
            Assert.That(difference, Is.GreaterThanOrEqualTo(0.0));

            if (prx.Proxy.Protocol == Protocol.IceRpc)
            {
                Assert.That(difference, Is.LessThan(50.0));
            }
        }

        /// <summary>Ensures that a request fails with OperationCanceledException after the invocation timeout expires.
        /// </summary>
        /// <param name="delay">The time in milliseconds to hold the dispatch to simulate an slow server.</param>
        /// <param name="timeout">The time in milliseconds used as the invocation timeout.</param>
        /// <param name="protocol">The protocol to use.</param>
        [TestCase(10000, 1000, "icerpc")]
        [TestCase(10000, 1000, "ice")]
        public async Task InvocationTimeout_WithTimeoutInterceptor(int delay, int timeout, string protocol)
        {
            DateTime? dispatchDeadline = null;
            bool invocationHasDeadline = false;
            await using ServiceProvider serviceProvider = new IntegrationTestServiceCollection()
                .UseProtocol(protocol)
                .AddTransient<IDispatcher>(_ =>
                {
                    var router = new Router();
                    router.Use(next => new InlineDispatcher(
                        async (request, cancel) =>
                        {
                            Assert.That(cancel.CanBeCanceled, Is.True);
                            dispatchDeadline = new Dispatch(request).Deadline;
                            await Task.Delay(TimeSpan.FromMilliseconds(delay), cancel);
                            return await next.DispatchAsync(request, cancel);
                        }));
                    router.Map<IGreeter>(new Greeter());
                    return router;
                })
                .BuildServiceProvider();

            // Setting a timeout with an interceptor
            var pipeline = new Pipeline();
            pipeline.UseTimeout(TimeSpan.FromMilliseconds(timeout));
            pipeline.Use(next => new InlineInvoker((request, cancel) =>
            {
                invocationHasDeadline = HasDeadline(request);
                return next.InvokeAsync(request, cancel);
            }));
            var prx = ServicePrx.FromConnection(serviceProvider.GetRequiredService<Connection>(), invoker: pipeline);
            var expectedDeadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(timeout);
            Assert.CatchAsync<OperationCanceledException>(async () => await prx.IcePingAsync());
            Assert.That(dispatchDeadline, Is.Not.Null);
            Assert.That(invocationHasDeadline, Is.True);

            double difference = (dispatchDeadline!.Value - expectedDeadline).TotalMilliseconds;
            Assert.That(difference, Is.GreaterThanOrEqualTo(0.0));

            if (prx.Proxy.Protocol == Protocol.IceRpc)
            {
                Assert.That(difference, Is.LessThan(50.0));
            }
        }

        /// <summary>Ensure that a request fails with OperationCanceledException after the invocation timeout expires.
        /// </summary>
        /// <param name="delay">The time in milliseconds to hold the dispatch to simulate a slow server.</param>
        /// <param name="timeout">The time in milliseconds used as the invocation timeout.</param>
        [TestCase(10000, 1000, "icerpc")]
        [TestCase(10000, 1000, "ice")]
        public async Task InvocationTimeout_InvocationPrevails(int delay, int timeout, string protocol)
        {
            DateTime? dispatchDeadline = null;
            bool invocationHasDeadline = false;

            await using ServiceProvider serviceProvider = new IntegrationTestServiceCollection()
                .AddTransient<IDispatcher>(_ =>
                {
                    var router = new Router();
                    router.Use(next => new InlineDispatcher(
                        async (request, cancel) =>
                        {
                            dispatchDeadline = new Dispatch(request).Deadline;
                            await Task.Delay(TimeSpan.FromMilliseconds(delay), cancel);
                            return await next.DispatchAsync(request, cancel);
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
            pipeline.UseTimeout(TimeSpan.FromMilliseconds(timeout * 10));
            pipeline.Use(next => new InlineInvoker((request, cancel) =>
            {
                Assert.That(cancel.CanBeCanceled, Is.True);
                invocationHasDeadline = HasDeadline(request);
                return next.InvokeAsync(request, cancel);
            }));
            prx = ServicePrx.FromConnection(connection, invoker: pipeline);
            var invocation = new Invocation { Timeout = TimeSpan.FromMilliseconds(timeout) };
            var expectedDeadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(timeout);
            Assert.CatchAsync<OperationCanceledException>(async () => await prx.IcePingAsync(invocation));
            Assert.That(dispatchDeadline, Is.Not.Null);
            Assert.That(invocationHasDeadline, Is.True);

            double difference = (dispatchDeadline!.Value - expectedDeadline).TotalMilliseconds;
            Assert.That(difference, Is.GreaterThanOrEqualTo(0.0));

            if (prx.Proxy.Protocol == Protocol.IceRpc)
            {
                Assert.That(difference, Is.LessThan(50.0));
            }
        }

        private static bool HasDeadline(OutgoingRequest request) =>
            request.Fields.ContainsKey((int)FieldKey.Deadline) ||
            request.FieldsOverrides.ContainsKey((int)FieldKey.Deadline);

        private class Greeter : Service, IGreeter
        {
            public ValueTask SayHelloAsync(string message, Dispatch dispatch, CancellationToken cancel) =>
                throw new NotImplementedException();
        }
    }
}
