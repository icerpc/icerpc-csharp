// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.Api
{
    [Parallelizable]
    public class InvocationTimeoutTests : ColocatedTest
    {
        /// <summary>Ensure that a request fails with OperationCanceledException after the invocation timemout expires.
        /// </summary>
        /// <param name="delay">The time in milliseconds to hold the dispatch to simulate an slow server.</param>
        /// <param name="timeout">The time in milliseconds used as the invocation timeout.</param>
        [TestCase(1000, 500)]
        public async Task InvocationTimeout_Throws_OperationCanceledException(int delay, int timeout)
        {
            DateTime dispatchDeadline = DateTime.UtcNow;
            DateTime invocationDeadline = DateTime.UtcNow;
            Server.Use(
                async (current, next, cancel) =>
                {
                    dispatchDeadline = current.Deadline;
                    await Task.Delay(TimeSpan.FromMilliseconds(delay), cancel);
                    return await next();
                });

            var prx = Server.AddWithUUID(new TestService(), IServicePrx.Factory).Clone(
                invocationTimeout: TimeSpan.FromMilliseconds(timeout),
                invocationInterceptors: ImmutableList.Create<InvocationInterceptor>(
                    async (target, request, next, cancel) =>
                    {
                        invocationDeadline = request.Deadline;
                        return await next(target, request, cancel);
                    }));

            Server.Activate();

            // Establish a connection
            var connection = await prx.GetConnectionAsync();

            DateTime expectedDeadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(timeout);
            Assert.CatchAsync<OperationCanceledException>(async () => await prx.IcePingAsync());
            Assert.AreEqual(dispatchDeadline, invocationDeadline);
            Assert.IsTrue(dispatchDeadline >= expectedDeadline);
            Assert.AreEqual(connection, await prx.GetConnectionAsync());
        }

        public class TestService : IAsyncGreeterService
        {
            public ValueTask SayHelloAsync(Current current, CancellationToken cancel) =>
                throw new NotImplementedException();
        }
    }
}
