// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Threading;
using System.Threading.Tasks;
using ZeroC.Ice;

namespace IceRpc.Tests.Api
{
    [Parallelizable]
    public class InvocationTimeoutTests : ColocatedTest
    {
        /// <summary>Ensure that a request fails with OperationCanceledException after the invocation timemout expires.
        /// </summary>
        /// <param name="delay">The time in milliseconds to hold the dispatch to simulate an slow server.</param>
        /// <param name="timeout">The time in milliseconds used as the invocation timeout.</param>
        [TestCase(500, 100)]
        public async Task InvocationTimeout_Throws_OperationCanceledException(int delay, int timeout)
        {
            Server.Use(
                async (current, next, cancel) =>
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(delay), cancel);
                    return await next();
                });

            var prx = Server.AddWithUUID(new TestService(), IServicePrx.Factory).Clone(
                invocationTimeout: TimeSpan.FromMilliseconds(timeout));

            await Server.ActivateAsync();

            // Establish a connection
            var connection = await prx.GetConnectionAsync();

            Assert.ThrowsAsync<OperationCanceledException>(async () => await prx.IcePingAsync());
            Assert.AreEqual(connection, await prx.GetConnectionAsync());
        }

        public class TestService : IAsyncGreeterService
        {
            public ValueTask SayHelloAsync(Current current, CancellationToken cancel) => throw new NotImplementedException();
        }
    }
}
