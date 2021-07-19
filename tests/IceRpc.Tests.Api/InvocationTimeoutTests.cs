// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.Api
{
    [Timeout(30000)]
    [Parallelizable(ParallelScope.All)]
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    public sealed class InvocationTimeoutTests : IAsyncDisposable
    {
        private readonly Server _server;

        public InvocationTimeoutTests()
        {
            _server = new Server
            {
                Endpoint = TestHelper.GetUniqueColocEndpoint()
            };
        }

        [TearDown]
        public async ValueTask DisposeAsync() => await _server.DisposeAsync();

        /// <summary>Ensure that a request fails with OperationCanceledException after the invocation timeout expires.
        /// </summary>
        /// <param name="delay">The time in milliseconds to hold the dispatch to simulate an slow server.</param>
        /// <param name="timeout">The time in milliseconds used as the invocation timeout.</param>
        [TestCase(10000, 1000)]
        public async Task InvocationTimeout_WithInvocation(int delay, int timeout)
        {
            DateTime? dispatchDeadline = null;
            DateTime? invocationDeadline = null;

            var router = new Router();
            router.Use(next => new InlineDispatcher(
                    async (current, cancel) =>
                    {
                        dispatchDeadline = current.Deadline;
                        await Task.Delay(TimeSpan.FromMilliseconds(delay), cancel);
                        return await next.DispatchAsync(current, cancel);
                    }));

            router.Map<IGreeter>(new Greeter());
            _server.Dispatcher = router;
            _server.Listen();

            await using var connection = new Connection { RemoteEndpoint = _server.ProxyEndpoint };

            var pipeline = new Pipeline();
            var prx = IServicePrx.FromConnection(connection, invoker: pipeline);

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
            Assert.AreEqual(dispatchDeadline, invocationDeadline);
            Assert.That(dispatchDeadline, Is.GreaterThanOrEqualTo(expectedDeadline));
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
            _server.Dispatcher = router;
            _server.Listen();

            await using var connection = new Connection { RemoteEndpoint = _server.ProxyEndpoint };

            // Setting a timeout with an interceptor
            var pipeline = new Pipeline();
            pipeline.Use(Interceptors.Timeout(TimeSpan.FromMilliseconds(timeout)));
            pipeline.Use(next => new InlineInvoker((request, cancel) =>
            {
                invocationDeadline = request.Deadline;
                return next.InvokeAsync(request, cancel);
            }));
            var prx = IServicePrx.FromConnection(connection, invoker: pipeline);
            var expectedDeadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(timeout);
            Assert.CatchAsync<OperationCanceledException>(async () => await prx.IcePingAsync());
            Assert.That(dispatchDeadline, Is.Not.Null);
            Assert.That(invocationDeadline, Is.Not.Null);
            Assert.AreEqual(dispatchDeadline, invocationDeadline);
            Assert.That(dispatchDeadline, Is.GreaterThanOrEqualTo(expectedDeadline));
        }

        /// <summary>Ensure that a request fails with OperationCanceledException after the invocation timeout expires.
        /// </summary>
        /// <param name="delay">The time in milliseconds to hold the dispatch to simulate an slow server.</param>
        /// <param name="timeout">The time in milliseconds used as the invocation timeout.</param>
        [TestCase(10000, 1000)]
        public async Task InvocationTimeout_InvocationPrevails(int delay, int timeout)
        {
            DateTime? dispatchDeadline = null;
            DateTime? invocationDeadline = null;

            var router = new Router();
            router.Use(next => new InlineDispatcher(
                    async (current, cancel) =>
                    {
                        dispatchDeadline = current.Deadline;
                        await Task.Delay(TimeSpan.FromMilliseconds(delay), cancel);
                        return await next.DispatchAsync(current, cancel);
                    }));

            router.Map<IGreeter>(new Greeter());
            _server.Dispatcher = router;
            _server.Listen();

            await using var connection = new Connection { RemoteEndpoint = _server.ProxyEndpoint };

            var pipeline = new Pipeline();
            var prx = IServicePrx.FromConnection(connection, invoker: pipeline);

            // Invocation timeout prevails
            pipeline = new Pipeline();
            pipeline.Use(Interceptors.Timeout(TimeSpan.FromMilliseconds(timeout * 10)));
            pipeline.Use(next => new InlineInvoker((request, cancel) =>
            {
                Assert.That(cancel.CanBeCanceled, Is.True);
                invocationDeadline = request.Deadline;
                return next.InvokeAsync(request, cancel);
            }));
            prx = IServicePrx.FromConnection(connection, invoker: pipeline);
            var invocation = new Invocation { Timeout = TimeSpan.FromMilliseconds(timeout) };
            var expectedDeadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(timeout);
            Assert.CatchAsync<OperationCanceledException>(async () => await prx.IcePingAsync(invocation));
            Assert.That(dispatchDeadline, Is.Not.Null);
            Assert.That(invocationDeadline, Is.Not.Null);
            Assert.AreEqual(dispatchDeadline, invocationDeadline);
            Assert.That(dispatchDeadline, Is.GreaterThanOrEqualTo(expectedDeadline));
        }

        public class Greeter : Service, IGreeter
        {
            public ValueTask SayHelloAsync(Dispatch dispatch, CancellationToken cancel) =>
                throw new NotImplementedException();
        }
    }
}
