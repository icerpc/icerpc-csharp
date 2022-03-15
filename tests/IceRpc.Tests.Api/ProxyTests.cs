// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Slice;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Tests.Api
{
    [Parallelizable(scope: ParallelScope.All)]
    [Timeout(5000)]
    // [Log(LogAttributeLevel.Information)]
    public class ProxyTests
    {
        [Test]
        public async Task Proxy_ReceiveProxyAsync()
        {
            var service = new ProxyTest();

            await using ServiceProvider serviceProvider = new IntegrationTestServiceCollection()
                .AddTransient<IDispatcher>(_ => service)
                .BuildServiceProvider();

            var prx = ProxyTestPrx.FromConnection(serviceProvider.GetRequiredService<Connection>());

            ProxyTestPrx? received = await prx.ReceiveProxyAsync();
            Assert.That(received, Is.Null);

            // Check that the received proxy "inherits" the invoker of the caller.
            service.Prx = ProxyTestPrx.FromPath("/foo");
            received = await prx.ReceiveProxyAsync();
            Assert.That(received?.Proxy.Invoker, Is.EqualTo(Proxy.DefaultInvoker));

            var pipeline = new Pipeline();
            prx.Proxy.Invoker = pipeline;
            received = await prx.ReceiveProxyAsync();
            Assert.That(received?.Proxy.Invoker, Is.EqualTo(pipeline));

            // Same with an endpoint
            service.Prx = ProxyTestPrx.Parse("icerpc://localhost/foo");
            received = await prx.ReceiveProxyAsync();
            Assert.That(received?.Proxy.Endpoint, Is.EqualTo(service.Prx?.Proxy.Endpoint));
            Assert.That(received?.Proxy.Invoker, Is.EqualTo(pipeline));
        }

        [Test]
        public async Task Proxy_SendProxyAsync()
        {
            var service = new ProxyTest();

            // First verify that the invoker of a proxy received over an incoming request is by default the default
            // invoker.
            await using ServiceProvider serviceProvider1 = new IntegrationTestServiceCollection()
                .AddTransient<IDispatcher>(_ => service)
                .BuildServiceProvider();

            var prx = ProxyTestPrx.FromConnection(serviceProvider1.GetRequiredService<Connection>());
            await prx.SendProxyAsync(prx);
            Assert.That(service.Prx, Is.Not.Null);
            Assert.That(service.Prx?.Proxy.Invoker, Is.EqualTo(Proxy.DefaultInvoker));

            // Now with a router and the ProxyInvoker middleware - we set the invoker on the proxy received by the
            // service.
            var pipeline = new Pipeline();

            await using ServiceProvider serviceProvider2 = new IntegrationTestServiceCollection()
                .AddTransient<IDispatcher>(_ =>
                {
                    var router = new Router();
                    router.Map<IProxyTest>(service);
                    router.UseFeature(new SliceDecodePayloadOptions { ProxyInvoker = pipeline });
                    return router;
                })
                .BuildServiceProvider();
            prx = ProxyTestPrx.FromConnection(serviceProvider2.GetRequiredService<Connection>());

            service.Prx = null;
            await prx.SendProxyAsync(prx);
            Assert.That(service.Prx, Is.Not.Null);
            Assert.That(service.Prx?.Proxy.Invoker, Is.EqualTo(pipeline));
        }

        public class Greeter : Service, IGreeter
        {
            public ValueTask SayHelloAsync(string message, Dispatch dispatch, CancellationToken cancel) => default;
        }

        private class ProxyTest : Service, IProxyTest
        {
            internal ProxyTestPrx? Prx { get; set; }

            public ValueTask<ProxyTestPrx?> ReceiveProxyAsync(Dispatch dispatch, CancellationToken cancel) => new(Prx);

            public ValueTask SendProxyAsync(ProxyTestPrx prx, Dispatch dispatch, CancellationToken cancel)
            {
                Prx = prx;
                return default;
            }
        }
    }
}
