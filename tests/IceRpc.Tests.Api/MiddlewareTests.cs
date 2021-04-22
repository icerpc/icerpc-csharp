// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.Api
{
    [Parallelizable]
    public class MiddlewareTests
    {
        /// <summary>Check that throwing an exception from a middleware aborts the dispatch.</summary>
        [Test]
        public async Task Middleware_Throw_AbortsDispatch()
        {
            await using var communicator = new Communicator();
            await using var server = new Server
            {
                Communicator = communicator,
                Endpoint = TestHelper.GetUniqueColocEndpoint()
            };

            var service = new TestService();

            var router = new Router();
            router.Use(next => new InlineDispatcher((current, cancel) => throw new ArgumentException("message")));
            router.Map("/test", service);

            server.Dispatcher = router;
            server.Listen();

            var prx = server.CreateProxy<IMiddlewareTestServicePrx>("/test");

            Assert.ThrowsAsync<UnhandledException>(() => prx.OpAsync());
            Assert.IsFalse(service.Called);
        }

        /// <summary>Ensure that middlewares are called in the expected order.</summary>
        [Test]
        public async Task Middleware_CallOrder()
        {
            await using var communicator = new Communicator();
            await using var server = new Server
            {
                Communicator = communicator,
                Endpoint = TestHelper.GetUniqueColocEndpoint()
            };

            var interceptorCalls = new List<string>();

            var router = new Router();

            router.Use(next => new InlineDispatcher(
                async (current, cancel) =>
                {
                    interceptorCalls.Add("Middlewares -> 0");
                    var result = await next.DispatchAsync(current, cancel);
                    interceptorCalls.Add("Middlewares <- 0");
                    return result;
                }));

            router.Use(next => new InlineDispatcher(
                async (current, cancel) =>
                {
                    interceptorCalls.Add("Middlewares -> 1");
                    var result = await next.DispatchAsync(current, cancel);
                    interceptorCalls.Add("Middlewares <- 1");
                    return result;
                }));

            router.Map("/test", new TestService());
            server.Dispatcher = router;
            server.Listen();
            var prx = server.CreateProxy<IServicePrx>("/test");
            await prx.IcePingAsync();

            Assert.AreEqual("Middlewares -> 0", interceptorCalls[0]);
            Assert.AreEqual("Middlewares -> 1", interceptorCalls[1]);
            Assert.AreEqual("Middlewares <- 1", interceptorCalls[2]);
            Assert.AreEqual("Middlewares <- 0", interceptorCalls[3]);
            Assert.AreEqual(4, interceptorCalls.Count);
        }

        public class TestService : IAsyncMiddlewareTestService
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
