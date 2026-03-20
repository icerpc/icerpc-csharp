// Copyright (c) ZeroC, Inc.

using IceRpc.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Ice.Generator.Tests;

[Parallelizable(scope: ParallelScope.All)]
public partial class ServiceProviderExtensionsTests
{
    [Test]
    public void Create_ice_proxy_with_no_params()
    {
        var serviceCollection =
            new ServiceCollection()
                .AddSingleton(InvalidInvoker.Instance)
                .AddSingleton<IPingable>(provider => provider.CreateIceProxy<PingableProxy>());

        var provider = serviceCollection.BuildServiceProvider(validateScopes: true);

        var proxy = (IIceProxy?)provider.GetService<IPingable>();
        Assert.That(proxy, Is.Not.Null);
        Assert.That(proxy.Invoker, Is.EqualTo(InvalidInvoker.Instance));
        Assert.That(proxy.ServiceAddress.Path, Is.EqualTo(PingableProxy.DefaultServicePath));
        Assert.That(proxy.EncodeOptions, Is.Null);
    }

    [Test]
    public void Create_ice_proxy_without_invoker_fails()
    {
        var serviceCollection =
            new ServiceCollection()
                .AddSingleton<IPingable>(provider => provider.CreateIceProxy<PingableProxy>());

        var provider = serviceCollection.BuildServiceProvider(validateScopes: true);
        Assert.That(() => provider.GetService<IPingable>(), Throws.InvalidOperationException);
    }
}
