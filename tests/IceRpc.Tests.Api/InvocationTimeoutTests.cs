// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Immutable;
using System.Threading.Tasks;
using NUnit.Framework;
using ZeroC.Ice;

namespace IceRpc.Tests.Api
{
    [Parallelizable]
    public class InvocationTimeoutTests
    {
        /// <summary>Ensure that a request fails with OperationCanceledException after the invocation timemout expires.
        /// </summary>
        /// <param name="delay">The time in milliseconds to hold the dispatch to simulate an slow server.</param>
        /// <param name="timeout">The time in milliseconds used as the invocation timeout.</param>
        [TestCase(500, 100)]
        public async Task DelayedInvocationTimeouts(int delay, int timeout)
        {
            await using var communicator = new Communicator();
            await using var adapter = communicator.CreateObjectAdapter("TestAdapter-1");

            adapter.DispatchInterceptors = ImmutableList.Create<DispatchInterceptor>(
                async (request, current, next, cancel) =>
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(delay), cancel);
                    return await next(request, current, cancel);
                });

            var prx = adapter.AddWithUUID(new TestService(), IObjectPrx.Factory).Clone(
                invocationTimeout: TimeSpan.FromMilliseconds(timeout));
            await adapter.ActivateAsync();
            
            // Establish a connection
            var connection = await prx.GetConnectionAsync();

            Assert.ThrowsAsync<OperationCanceledException>(async () => await prx.IcePingAsync());
            Assert.AreEqual(connection, await prx.GetConnectionAsync());
        }

        public class TestService : IAsyncInvocationTimeoutTestService
        {
        }
    }
}