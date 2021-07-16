// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.Api
{
    [Timeout(30000)]
    [Parallelizable]
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

        [OneTimeTearDown]
        public async ValueTask DisposeAsync() => await _server.DisposeAsync();

        /// <summary>Ensure that a request fails with OperationCanceledException after the invocation timeout expires.
        /// </summary>
        /// <param name="delay">The time in milliseconds to hold the dispatch to simulate an slow server.</param>
        /// <param name="timeout">The time in milliseconds used as the invocation timeout.</param>
        [TestCase(10000, 1000)]
        public async Task InvocationTimeout_Throws_OperationCanceledExceptionAsync(int delay, int timeout)
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
            var prx = ServicePrx.FromConnection(connection, invoker: pipeline);

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

        public class Greeter : Service, IGreeter
        {
            public ValueTask SayHelloAsync(Dispatch dispatch, CancellationToken cancel) =>
                throw new NotImplementedException();
        }
    }
}
