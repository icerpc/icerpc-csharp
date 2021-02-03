// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Immutable;
using System.Threading.Tasks;
using NUnit.Framework;
using ZeroC.Ice;

namespace IceRpc.Tests.InvocationTimeout
{
    [Parallelizable]
    public class AllTests : FunctionalTest
    {
        /// <summary>Ensure that a request fails with OperationCanceledException after the invocation timemout expires.
        /// </summary>
        /// <param name="delay">The time in milliseconds to hold the dispatch to simulate an slow server.</param>
        /// <param name="timeout">The time in milliseconds used as the invocation timeout.</param>
        [TestCase(500, 100)]
        public async Task DelayedInvocationTimeouts(int delay, int timeout)
        {
            await using var adapter = Communicator.CreateObjectAdapter(
                "TestAdapter-1",
                new ObjectAdapterOptions()
                {
                    Endpoints = GetTestEndpoint(port: 1)
                });

            adapter.DispatchInterceptors = ImmutableList.Create<DispatchInterceptor>(
                async (request, current, next, cancel) =>
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(delay), cancel);
                    return await next(request, current, cancel);
                });
            adapter.Add("test", new TestService());
            await adapter.ActivateAsync();

            var prx = IObjectPrx.Parse(GetTestProxy("test", port: 1), Communicator).Clone(
                invocationTimeout: TimeSpan.FromMilliseconds(timeout));
            
            // Establish a connection
            var connection = await prx.GetConnectionAsync();

            Assert.ThrowsAsync<OperationCanceledException>(async () => await prx.IcePingAsync());
            Assert.AreEqual(connection, await prx.GetConnectionAsync());
        }
    }

    public class TestService : IAsyncTestService
    {
    }
}