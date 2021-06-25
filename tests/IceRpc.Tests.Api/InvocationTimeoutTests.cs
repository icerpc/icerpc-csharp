// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.Api
{
    [Parallelizable]
    [Timeout(5000)]
    public class InvocationTimeoutTests
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
        public async Task ShutdownAsync()
        {
            await _server.DisposeAsync();
        }

        /// <summary>Ensure that a request fails with OperationCanceledException after the invocation timeout expires.
        /// </summary>
        /// <param name="delay">The time in milliseconds to hold the dispatch to simulate an slow server.</param>
        /// <param name="timeout">The time in milliseconds used as the invocation timeout.</param>
        [TestCase(1000)]
        public async Task InvocationTimeout_Throws_OperationCanceledExceptionAsync(int timeout)
        {
            DateTime? dispatchDeadline = null;
            DateTime? invocationDeadline = null;
            var dispatchSemaphore = new SemaphoreSlim(0);
            var router = new Router();
            router.Use(next => new InlineDispatcher(
                    async (current, cancel) =>
                    {
                        dispatchDeadline = current.Deadline;
                        await dispatchSemaphore.WaitAsync(CancellationToken.None);
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
            dispatchSemaphore.Release();
            Assert.That(dispatchDeadline, Is.Not.Null);
            Assert.That(invocationDeadline, Is.Not.Null);
            Assert.AreEqual(dispatchDeadline, invocationDeadline);
            Assert.That(dispatchDeadline, Is.GreaterThanOrEqualTo(expectedDeadline));
        }

        public class Greeter : IGreeter
        {
            public ValueTask SayHelloAsync(Dispatch dispatch, CancellationToken cancel) =>
                throw new NotImplementedException();
        }
    }
}
