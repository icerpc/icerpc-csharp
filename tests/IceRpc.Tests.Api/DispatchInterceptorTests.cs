// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ZeroC.Ice;

namespace IceRpc.Tests.Api
{
    [Parallelizable]
    public class DispatchInterceptorTests
    {
        /// <summary>Check that throwing an exception from a dispatch interceptor aborts the dispatch.</summary>
        [Test]
        public async Task DispatchInterceptor_Throw_AbortsDispatch()
        {
            await using var communicator = new Communicator();
            await using var adapter = new ObjectAdapter(communicator)
            {
                DispatchInterceptor = (request, current, next, cancel) => throw new ArgumentException()
            };
            var service = new TestService();
            var prx = adapter.AddWithUUID(service, IDispatchInterceptorTestServicePrx.Factory);
            await adapter.ActivateAsync();

            Assert.ThrowsAsync<UnhandledException>(() => prx.OpAsync());
            Assert.IsFalse(service.Called);
        }

        /// <summary>Ensure that object adapter dispatch interceptors are called in the expected order.</summary>
        [Test]
        public async Task DispatchInterceptor_CallOrder()
        {
            await using var communicator = new Communicator();
            await using var adapter = new ObjectAdapter(communicator);
            var interceptorCalls = new List<string>();
            adapter.DispatchInterceptor += async (request, current, next, cancel) =>
            {
                interceptorCalls.Add("DispatchInterceptor -> 0");
                var result = await next(request, current, cancel);
                interceptorCalls.Add("DispatchInterceptor <- 0");
                return result;
            };
            adapter.DispatchInterceptor += async (request, current, next, cancel) =>
            {
                interceptorCalls.Add("DispatchInterceptor -> 1");
                var result = await next(request, current, cancel);
                interceptorCalls.Add("DispatchInterceptor <- 1");
                return result;
            };
            var prx = adapter.AddWithUUID(new TestService(), IObjectPrx.Factory);
            await adapter.ActivateAsync();

            await prx.IcePingAsync();

            Assert.AreEqual("DispatchInterceptor -> 0", interceptorCalls[0]);
            Assert.AreEqual("DispatchInterceptor -> 1", interceptorCalls[1]);
            Assert.AreEqual("DispatchInterceptor <- 1", interceptorCalls[2]);
            Assert.AreEqual("DispatchInterceptor <- 0", interceptorCalls[3]);
            Assert.AreEqual(4, interceptorCalls.Count);
        }

        public class TestService : IAsyncDispatchInterceptorTestService
        {
            public bool Called { get; private set; }
            public ValueTask OpAsync(Current current, CancellationToken cancel)
            {
                Called = true;
                return default;
            }
        }
    }
}
