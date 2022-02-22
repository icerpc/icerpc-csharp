// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Slice;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

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
            var service = new Greeter();
            await using ServiceProvider serviceProvider = new IntegrationTestServiceCollection()
                .AddTransient<IDispatcher>(_ =>
                {
                    var router = new Router();
                    router.Use(next => new InlineDispatcher((request, cancel) => throw new ArgumentException("message")));
                    router.Map<IGreeter>(service);
                    return router;
                })
                .BuildServiceProvider();

            var prx = GreeterPrx.FromConnection(serviceProvider.GetRequiredService<Connection>());
            DispatchException dispatchException = Assert.ThrowsAsync<DispatchException>(
                () => prx.SayHelloAsync("hello"));
            Assert.That(dispatchException.ErrorCode, Is.EqualTo(DispatchErrorCode.UnhandledException));
            Assert.That(service.Called, Is.False);
        }

        /// <summary>Ensure that middleware are called in the expected order.</summary>
        [Test]
        public async Task Middleware_CallOrder()
        {
            var middlewareCalls = new List<string>();

            await using ServiceProvider serviceProvider = new IntegrationTestServiceCollection()
                .AddTransient<IDispatcher>(_ =>
                {
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
                    return router;
                })
                .BuildServiceProvider();

            var prx = GreeterPrx.FromConnection(serviceProvider.GetRequiredService<Connection>());
            await new ServicePrx(prx.Proxy).IcePingAsync();

            Assert.That(middlewareCalls[0], Is.EqualTo("Middlewares -> 0"));
            Assert.That(middlewareCalls[1], Is.EqualTo("Middlewares -> 1"));
            Assert.That(middlewareCalls[2], Is.EqualTo("Middlewares <- 1"));
            Assert.That(middlewareCalls[3], Is.EqualTo("Middlewares <- 0"));
            Assert.That(middlewareCalls.Count, Is.EqualTo(4));
        }

        public class Greeter : Service, IGreeter
        {
            public bool Called { get; private set; }

            public ValueTask SayHelloAsync(string message, Dispatch dispatch, CancellationToken cancel)
            {
                Called = true;
                return default;
            }
        }
    }
}
