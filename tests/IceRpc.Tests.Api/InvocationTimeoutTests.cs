// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.Api
{
    [Parallelizable]
    public class InvocationTimeoutTests
    {
        private readonly Communicator _communicator;
        private readonly Server _server;

        public InvocationTimeoutTests()
        {
            _communicator = new Communicator();
            _server = new Server
            {
                Communicator = _communicator,
                Endpoint = TestHelper.GetUniqueColocEndpoint()
            };
        }

        [OneTimeTearDown]
        public async Task ShutdownAsync()
        {
            await _server.DisposeAsync();
            await _communicator.DisposeAsync();
        }

        /// <summary>Ensure that a request fails with OperationCanceledException after the invocation timeout expires.
        /// </summary>
        /// <param name="delay">The time in milliseconds to hold the dispatch to simulate an slow server.</param>
        /// <param name="timeout">The time in milliseconds used as the invocation timeout.</param>
        [TestCase(1000, 500)]
        public void InvocationTimeout_Throws_OperationCanceledException(int delay, int timeout)
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

            router.Map("/test", new TestService());
            _server.Dispatcher = router;
            _server.Listen();

            IServicePrx prx = _server.CreateProxy<IServicePrx>("/test");
            prx.InvocationTimeout = TimeSpan.FromMilliseconds(timeout);
            prx.Use(next => new InlineInvoker((request, cancel) =>
            {
                invocationDeadline = request.Deadline;
                return next.InvokeAsync(request, cancel);
            }));

            DateTime expectedDeadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(timeout);
            Assert.CatchAsync<OperationCanceledException>(async () => await prx.IcePingAsync());
            Assert.That(dispatchDeadline, Is.Not.Null);
            Assert.That(invocationDeadline, Is.Not.Null);
            Assert.AreEqual(dispatchDeadline, invocationDeadline);
            Assert.That(dispatchDeadline, Is.GreaterThanOrEqualTo(expectedDeadline));

        }

        public class TestService : IGreeterService
        {
            public ValueTask SayHelloAsync(Dispatch dispatch, CancellationToken cancel) =>
                throw new NotImplementedException();
        }
    }
}
