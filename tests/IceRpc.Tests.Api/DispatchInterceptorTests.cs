// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Threading;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using NUnit.Framework;
using System;
using ZeroC.Ice;

namespace IceRpc.Tests.Api
{
    [Parallelizable]
    public class DispatchInterceptorTests
    {
        [Test]
        public async Task ThrowUserExceptionFromDispatchInterceptor()
        {
            await using var communicator = new Communicator();
            await using var adapter = communicator.CreateObjectAdapter("TestAdapter-1");

            adapter.DispatchInterceptors = ImmutableList.Create<DispatchInterceptor>(
                (request, current, next, cancel) => throw new DispatchInterceptorForbiddenException());
            var prx = adapter.AddWithUUID(new TestService(), IObjectPrx.Factory);
            await adapter.ActivateAsync();

            Assert.ThrowsAsync<DispatchInterceptorForbiddenException>(() => prx.IcePingAsync());
        }

        [Test]
        public async Task ThrowSystemExceptionFromDispatchInterceptor()
        {
            await using var communicator = new Communicator();
            await using var adapter = communicator.CreateObjectAdapter("TestAdapter-1");

            adapter.DispatchInterceptors = ImmutableList.Create<DispatchInterceptor>(
                (request, current, next, cancel) => throw new ArgumentException());
            var prx = adapter.AddWithUUID(new TestService(), IObjectPrx.Factory);
            await adapter.ActivateAsync();

            Assert.ThrowsAsync<UnhandledException>(() => prx.IcePingAsync());
        }

        /// <summary>Ensure that default dispatch interceptors are called in the expected order.</summary>
        [Test]
        public async Task DefaultDispatchInterceptorCallOrder()
        {
            await using var communicator = new Communicator();

            var interceptorCalls = new List<string>();
            communicator.DefaultDispatchInterceptors = ImmutableList.Create<DispatchInterceptor>(
                async (request, current, next, cancel) =>
                    {
                        interceptorCalls.Add("DefaultDispatchInterceptors -> 0");
                        var result = await next(request, current, cancel);
                        interceptorCalls.Add("DefaultDispatchInterceptors <- 0");
                        return result;
                    },
                async (request, current, next, cancel) =>
                    {
                        interceptorCalls.Add("DefaultDispatchInterceptors -> 1");
                        var result = await next(request, current, cancel);
                        interceptorCalls.Add("DefaultDispatchInterceptors <- 1");
                        return result;
                    });

            await using var adapter = communicator.CreateObjectAdapter("TestAdapter-1");
            var prx = adapter.AddWithUUID(new TestService(), IObjectPrx.Factory);
            await adapter.ActivateAsync();

            await prx.IcePingAsync();

            Assert.AreEqual("DefaultDispatchInterceptors -> 0", interceptorCalls[0]);
            Assert.AreEqual("DefaultDispatchInterceptors -> 1", interceptorCalls[1]);
            Assert.AreEqual("DefaultDispatchInterceptors <- 1", interceptorCalls[2]);
            Assert.AreEqual("DefaultDispatchInterceptors <- 0", interceptorCalls[3]);
            Assert.AreEqual(4, interceptorCalls.Count);
        }

        /// <summary>Ensure that object adapter dispatch interceptors are called in the expected order.</summary>
        [Test]
        public async Task ObjectAdapterDispatchInterceptorCallOrder()
        {
            await using var communicator = new Communicator();
            await using var adapter = communicator.CreateObjectAdapter("TestAdapter-1");
            var interceptorCalls = new List<string>();
            adapter.DispatchInterceptors = ImmutableList.Create<DispatchInterceptor>(
                async (request, current, next, cancel) =>
                {
                    interceptorCalls.Add("AdapterDispatchInterceptors -> 0");
                    var result = await next(request, current, cancel);
                    interceptorCalls.Add("AdapterDispatchInterceptors <- 0");
                    return result;
                },
                async (request, current, next, cancel) =>
                {
                    interceptorCalls.Add("AdapterDispatchInterceptors -> 1");
                    var result = await next(request, current, cancel);
                    interceptorCalls.Add("AdapterDispatchInterceptors <- 1");
                    return result;
                });
            var prx = adapter.AddWithUUID(new TestService(), IObjectPrx.Factory);
            await adapter.ActivateAsync();

            await prx.IcePingAsync();

            Assert.AreEqual("AdapterDispatchInterceptors -> 0", interceptorCalls[0]);
            Assert.AreEqual("AdapterDispatchInterceptors -> 1", interceptorCalls[1]);
            Assert.AreEqual("AdapterDispatchInterceptors <- 1", interceptorCalls[2]);
            Assert.AreEqual("AdapterDispatchInterceptors <- 0", interceptorCalls[3]);
            Assert.AreEqual(4, interceptorCalls.Count);
        }

        public class TestService : IAsyncDispatchInterceptorTestService
        {
        }
    }
}
