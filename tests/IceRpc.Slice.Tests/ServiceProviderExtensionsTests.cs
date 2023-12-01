// Copyright (c) ZeroC, Inc.

using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Slice.Tests;

[Parallelizable(scope: ParallelScope.All)]
public partial class ServiceProviderExtensionsTests
{
    [Test]
    public void Create_slice_proxy_with_no_params()
    {
        var serviceCollection =
            new ServiceCollection()
                .AddSingleton(InvalidInvoker.Instance)
                .AddSingleton<IMyOperationsA>(provider => provider.CreateSliceProxy<MyOperationsAProxy>());

        var provider = serviceCollection.BuildServiceProvider(validateScopes: true);

        var proxy = (IProxy?)provider.GetService<IMyOperationsA>();
        Assert.That(proxy, Is.Not.Null);
        Assert.That(proxy.Invoker, Is.EqualTo(InvalidInvoker.Instance));
        Assert.That(proxy.ServiceAddress.Path, Is.EqualTo(MyOperationsAProxy.DefaultServicePath));
        Assert.That(proxy.EncodeOptions, Is.Null);
    }

    [Test]
    public void Create_slice_proxy_without_invoker_fails()
    {
        var serviceCollection =
            new ServiceCollection()
                .AddSingleton<IMyOperationsA>(provider => provider.CreateSliceProxy<MyOperationsAProxy>());

        var provider = serviceCollection.BuildServiceProvider(validateScopes: true);
        Assert.That(() => provider.GetService<IMyOperationsA>(), Throws.InvalidOperationException);
    }
}
