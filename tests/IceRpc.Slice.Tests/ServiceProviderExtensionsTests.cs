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

        IMyOperationsA? proxy = provider.GetService<IMyOperationsA>();

        Assert.That(proxy, Is.Not.Null);
        Assert.That(proxy, Is.InstanceOf<IProxy>());
        IProxy? iproxy = (IProxy?)proxy;
        Assert.That(iproxy.Invoker, Is.EqualTo(InvalidInvoker.Instance));
        Assert.That(iproxy.ServiceAddress.Path, Is.EqualTo(MyOperationsAProxy.DefaultServicePath));
        Assert.That(iproxy.EncodeOptions, Is.Null);
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
