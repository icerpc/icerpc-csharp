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
            await using var server = new Server(communicator,
                                                new ServerOptions() { ColocationScope = ColocationScope.Communicator });

            var router = IRouter.CreateDefault();
            router.Use(Middleware.From((current, next, cancel) => throw new ArgumentException()));
            var service = new TestService();
            router.Map("/test", service);
            var prx = IDispatchInterceptorTestServicePrx.Factory.Create(server, "/test");
            server.Activate(router);

            Assert.ThrowsAsync<UnhandledException>(() => prx.OpAsync());
            Assert.IsFalse(service.Called);
        }

        /// <summary>Ensure that middlewares are called in the expected order.</summary>
        [Test]
        public async Task DispatchInterceptor_CallOrder()
        {
            await using var communicator = new Communicator();
            await using var server = new Server(communicator,
                                                new ServerOptions() { ColocationScope = ColocationScope.Communicator });
            var interceptorCalls = new List<string>();

            var router = IRouter.CreateDefault();

            // Simple middleware followed by regular middleware
            router.Use(Middleware.From(
                    async (current, next, cancel) =>
                    {
                        interceptorCalls.Add("DispatchInterceptors -> 0");
                        var result = await next();
                        interceptorCalls.Add("DispatchInterceptors <- 0");
                        return result;
                    }));

            router.Use(next => IDispatcher.FromInlineDispatcher
                (async (current, cancel) =>
                {
                    interceptorCalls.Add("DispatchInterceptors -> 1");
                    var result = await next.DispatchAsync(current, cancel);
                    interceptorCalls.Add("DispatchInterceptors <- 1");
                    return result;
                }));

            router.Map("/test", new TestService());
            var prx = IServicePrx.Factory.Create(server, "/test");

            server.Activate(router);

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
