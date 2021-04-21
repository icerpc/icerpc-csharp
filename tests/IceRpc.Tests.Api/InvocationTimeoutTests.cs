// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Collections.Immutable;
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
            _server = new Server { Communicator = _communicator };
        }

        [OneTimeTearDown]
        public async Task ShutdownAsync()
        {
            await _server.DisposeAsync();
            await _communicator.DisposeAsync();
        }

        /// <summary>Ensure that a request fails with OperationCanceledException after the invocation timemout expires.
        /// </summary>
        /// <param name="delay">The time in milliseconds to hold the dispatch to simulate an slow server.</param>
        /// <param name="timeout">The time in milliseconds used as the invocation timeout.</param>
        [TestCase(1000, 500)]
        public void InvocationTimeout_Throws_OperationCanceledException(int delay, int timeout)
        {
            DateTime dispatchDeadline = DateTime.UtcNow;
            DateTime invocationDeadline = DateTime.UtcNow;

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

            var prx = _server.CreateProxy<IServicePrx>("/test");
            prx.InvocationTimeout = TimeSpan.FromMilliseconds(timeout);
            prx.InvocationInterceptors = ImmutableList.Create<InvocationInterceptor>(
                    async (target, request, next, cancel) =>
                    {
                        invocationDeadline = request.Deadline;
                        return await next(target, request, cancel);
                    });

            DateTime expectedDeadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(timeout);
            Assert.CatchAsync<OperationCanceledException>(async () => await prx.IcePingAsync());
            Assert.AreEqual(dispatchDeadline, invocationDeadline);
            Assert.IsTrue(dispatchDeadline >= expectedDeadline);
        }

        public class TestService : IAsyncGreeterService
        {
            public ValueTask SayHelloAsync(Current current, CancellationToken cancel) =>
                throw new NotImplementedException();
        }
    }
}
