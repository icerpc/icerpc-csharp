// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc;
using IceRpc.Configure;
using IceRpc.Slice;
using IceRpc.Tests;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IntegrationTests;

public class ProxyTests
{
    /// <summary>Verifies that a proxy received over an outgoing connection inherits the callers invoker.</summary>
    [Test]
    public async Task Proxy_received_over_an_outgoing_connection_inherits_the_callers_invoker()
    {
        await using ServiceProvider serviceProvider = new IntegrationTestServiceCollection()
            .AddTransient<IDispatcher>(_ => new ReceiveProxyTest())
            .BuildServiceProvider();
        var invoker = new Pipeline();
        var prx = ReceiveProxyTestPrx.FromConnection(
            serviceProvider.GetRequiredService<Connection>(),
            invoker: invoker);

        ReceiveProxyTestPrx received = await prx.ReceiveProxyAsync();

        Assert.That(received.Proxy.Invoker, Is.EqualTo(invoker));
    }

    /// <summary>Verifies that a proxy received over an incoming connection uses the default invoker.</summary>
    [Test]
    public async Task Proxy_received_over_an_incoming_connection_uses_the_default_invoker()
    {
        var service = new SendProxyTest();
        await using ServiceProvider serviceProvider = new IntegrationTestServiceCollection()
            .AddTransient<IDispatcher>(_ => service)
            .BuildServiceProvider();
        var prx = SendProxyTestPrx.FromConnection(serviceProvider.GetRequiredService<Connection>());

        await prx.SendProxyAsync(prx);

        Assert.That(service.ReceivedPrx, Is.Not.Null);
        Assert.That(service.ReceivedPrx.Value.Proxy.Invoker, Is.EqualTo(Proxy.DefaultInvoker));
    }

    /// <summary>Verifies that the proxy invoker of the <see cref="SliceDecodePayloadOptions"/> is used for proxies
    /// received over an incoming connection.</summary>
    [Test]
    public async Task Proxy_invoker_is_set_to_the_slice_decode_options_feature_proxy_invoker()
    {
        var service = new SendProxyTest();
        var pipeline = new Pipeline();
        await using ServiceProvider serviceProvider = new IntegrationTestServiceCollection()
            .AddTransient<IDispatcher>(_ =>
            {
                var router = new Router();
                router.Map<ISendProxyTest>(service);
                router.UseFeature(new SliceDecodePayloadOptions { ProxyInvoker = pipeline });
                return router;
            })

            .BuildServiceProvider();
        var prx = SendProxyTestPrx.FromConnection(serviceProvider.GetRequiredService<Connection>());

        await prx.SendProxyAsync(prx);

        Assert.That(service.ReceivedPrx, Is.Not.Null);
        Assert.That(service.ReceivedPrx.Value.Proxy.Invoker, Is.EqualTo(pipeline));

    }

    private class ReceiveProxyTest : Service, IReceiveProxyTest
    {
        public ValueTask<ReceiveProxyTestPrx> ReceiveProxyAsync(Dispatch dispatch, CancellationToken cancel) =>
            new(ReceiveProxyTestPrx.FromPath("/hello"));
    }

    private class SendProxyTest : Service, ISendProxyTest
    {
        public SendProxyTestPrx? ReceivedPrx { get; private set; }

        public ValueTask SendProxyAsync(
            SendProxyTestPrx proxy,
            Dispatch dispatch,
            CancellationToken cancel = default)
        {
            ReceivedPrx = proxy;
            return default;
        }
    }
}
