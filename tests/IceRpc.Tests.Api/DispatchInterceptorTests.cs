// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace IceRpc.Tests.Api
{
    // TODO: rename to middleware

    [Parallelizable]
    public class DispatchInterceptorTests
    {
        /// <summary>Check that throwing an exception from a dispatch interceptor aborts the dispatch.</summary>
        [Test]
        public async Task DispatchInterceptor_Throw_AbortsDispatch()
        {
            await using var communicator = new Communicator();
            await using var server = new Server { Communicator = communicator };

            var router = new Router();
            router.Use(next => new InlineDispatcher((current, cancel) => throw new ArgumentException("message")));
            var service = new TestService();
            router.Map("/test", service);
            var prx = IDispatchInterceptorTestServicePrx.Factory.Create(server, "/test");

            server.Dispatcher = router;
            _ = server.ListenAndServeAsync();

            Assert.ThrowsAsync<UnhandledException>(() => prx.OpAsync());
            Assert.IsFalse(service.Called);
        }

        /// <summary>Ensure that middlewares are called in the expected order.</summary>
        [Test]
        public async Task DispatchInterceptor_CallOrder()
        {
            await using var communicator = new Communicator();
            await using var server = new Server { Communicator = communicator };

            var interceptorCalls = new List<string>();

            var router = new Router();

            router.Use(next => new InlineDispatcher(
                async (current, cancel) =>
                {
                    interceptorCalls.Add("DispatchInterceptors -> 0");
                    var result = await next.DispatchAsync(current, cancel);
                    interceptorCalls.Add("DispatchInterceptors <- 0");
                    return result;
                }));

            router.Use(next => new InlineDispatcher(
                async (current, cancel) =>
                {
                    interceptorCalls.Add("DispatchInterceptors -> 1");
                    var result = await next.DispatchAsync(current, cancel);
                    interceptorCalls.Add("DispatchInterceptors <- 1");
                    return result;
                }));

            router.Map("/test", new TestService());
            var prx = IServicePrx.Factory.Create(server, "/test");

            server.Dispatcher = router;
            _ = server.ListenAndServeAsync();
            await prx.IcePingAsync();

            Assert.AreEqual("DispatchInterceptors -> 0", interceptorCalls[0]);
            Assert.AreEqual("DispatchInterceptors -> 1", interceptorCalls[1]);
            Assert.AreEqual("DispatchInterceptors <- 1", interceptorCalls[2]);
            Assert.AreEqual("DispatchInterceptors <- 0", interceptorCalls[3]);
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
