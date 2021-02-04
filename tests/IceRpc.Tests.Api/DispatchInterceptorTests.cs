// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using ZeroC.Ice;

namespace IceRpc.Tests.Api
{
    [Parallelizable]
    public class DispatchInterceptorTests
    {
        [Test]
        public async Task DispatchInterceptor_Throws_UserException()
        {
            await using var communicator = new Communicator();
            await using var adapter = new ObjectAdapter(communicator)
            {
                DispatchInterceptors = ImmutableList.Create<DispatchInterceptor>(
                    (request, current, next, cancel) => throw new DispatchInterceptorForbiddenException())
            };
            var prx = adapter.AddWithUUID(new TestService(), IObjectPrx.Factory);
            await adapter.ActivateAsync();

            Assert.ThrowsAsync<DispatchInterceptorForbiddenException>(() => prx.IcePingAsync());
        }

        [Test]
        public async Task DispatchInterceptor_Throws_SystemExceptionFrom()
        {
            await using var communicator = new Communicator();
            await using var adapter = new ObjectAdapter(communicator)
            {
                DispatchInterceptors = ImmutableList.Create<DispatchInterceptor>(
                    (request, current, next, cancel) => throw new ArgumentException())
            };
            var prx = adapter.AddWithUUID(new TestService(), IObjectPrx.Factory);
            await adapter.ActivateAsync();

            Assert.ThrowsAsync<UnhandledException>(() => prx.IcePingAsync());
        }

        /// <summary>Ensure that object adapter dispatch interceptors are called in the expected order.</summary>
        [Test]
        public async Task DispatchInterceptor_CallOrder()
        {
            await using var communicator = new Communicator();
            await using var adapter = new ObjectAdapter(communicator);
            var interceptorCalls = new List<string>();
            adapter.DispatchInterceptors = ImmutableList.Create<DispatchInterceptor>(
                async (request, current, next, cancel) =>
                {
                    interceptorCalls.Add("DispatchInterceptors -> 0");
                    var result = await next(request, current, cancel);
                    interceptorCalls.Add("DispatchInterceptors <- 0");
                    return result;
                },
                async (request, current, next, cancel) =>
                {
                    interceptorCalls.Add("DispatchInterceptors -> 1");
                    var result = await next(request, current, cancel);
                    interceptorCalls.Add("DispatchInterceptors <- 1");
                    return result;
                });
            var prx = adapter.AddWithUUID(new TestService(), IObjectPrx.Factory);
            await adapter.ActivateAsync();

            await prx.IcePingAsync();

            Assert.AreEqual("DispatchInterceptors -> 0", interceptorCalls[0]);
            Assert.AreEqual("DispatchInterceptors -> 1", interceptorCalls[1]);
            Assert.AreEqual("DispatchInterceptors <- 1", interceptorCalls[2]);
            Assert.AreEqual("DispatchInterceptors <- 0", interceptorCalls[3]);
            Assert.AreEqual(4, interceptorCalls.Count);
        }

        public class TestService : IAsyncDispatchInterceptorTestService
        {
        }
    }
}
