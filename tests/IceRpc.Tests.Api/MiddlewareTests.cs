// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.Api
{
    [Parallelizable]
    [Timeout(30000)]
    public class MiddlewareTests
    {
        /// <summary>Check that throwing an exception from a middleware aborts the dispatch.</summary>
        [Test]
        public async Task Middleware_Throw_AbortsDispatch()
        {
            await using var server = new Server
            {
                Endpoint = TestHelper.GetUniqueColocEndpoint()
            };

            await using var connection = new Connection { RemoteEndpoint = server.ProxyEndpoint };

            var service = new Greeter();

            var router = new Router();
            router.Use(next => new InlineDispatcher((request, cancel) => throw new ArgumentException("message")));
            router.Map<IGreeter>(service);

            server.Dispatcher = router;
            server.Listen();

            var prx = GreeterPrx.FromConnection(connection);

            Assert.ThrowsAsync<UnhandledException>(() => prx.SayHelloAsync());
            Assert.That(service.Called, Is.False);
        }

        /// <summary>Ensure that middleware are called in the expected order.</summary>
        [Test]
        public async Task Middleware_CallOrder()
        {
            await using var server = new Server
            {
                Endpoint = TestHelper.GetUniqueColocEndpoint()
            };

            var middlewareCalls = new List<string>();

            var router = new Router();

            router.Use(next => new InlineDispatcher(
                async (request, cancel) =>
                {
                    middlewareCalls.Add("Middlewares -> 0");
                    OutgoingResponse result = await next.DispatchAsync(request, cancel);
                    middlewareCalls.Add("Middlewares <- 0");
                    return result;
                }));

            router.Use(next => new InlineDispatcher(
                async (request, cancel) =>
                {
                    middlewareCalls.Add("Middlewares -> 1");
                    OutgoingResponse result = await next.DispatchAsync(request, cancel);
                    middlewareCalls.Add("Middlewares <- 1");
                    return result;
                }));

            router.Map<IGreeter>(new Greeter());
            server.Dispatcher = router;
            server.Listen();

            await using var connection = new Connection { RemoteEndpoint = server.ProxyEndpoint };

            var prx = GreeterPrx.FromConnection(connection);
            await prx.IcePingAsync();

            Assert.AreEqual("Middlewares -> 0", middlewareCalls[0]);
            Assert.AreEqual("Middlewares -> 1", middlewareCalls[1]);
            Assert.AreEqual("Middlewares <- 1", middlewareCalls[2]);
            Assert.AreEqual("Middlewares <- 0", middlewareCalls[3]);
            Assert.AreEqual(4, middlewareCalls.Count);
        }

        public class Greeter : Service, IGreeter
        {
            public bool Called { get; private set; }

            public ValueTask SayHelloAsync(Dispatch dispatch, CancellationToken cancel)
            {
                Called = true;
                return default;
            }
        }
    }
}
