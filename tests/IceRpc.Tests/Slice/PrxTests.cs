// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Tests;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Slice.Tests;

/// <summary>Test IPrx extension methods other than InvokeAsync.</summary>
[Parallelizable(scope: ParallelScope.All)]
public class PrxTests
{
    [Test]
    public async Task Downcast_prx_with_as_sync_succeeds()
    {
        await using var provider = new SliceTestServiceCollection()
            .UseDispatcher(new MyDerivedInterface())
            .BuildServiceProvider();
        var prx = MyBaseInterfacePrx.FromConnection(provider.GetRequiredService<Connection>());

        MyDerivedInterfacePrx? derived = await prx.AsAsync<MyDerivedInterfacePrx>();

        Assert.That(derived, Is.Not.Null);
    }

    [Test]
    public async Task Downcast_prx_with_as_aync_fails()
    {
        await using var provider = new SliceTestServiceCollection()
            .UseDispatcher(new MyBaseInterface())
            .BuildServiceProvider();
        var prx = MyBaseInterfacePrx.FromConnection(provider.GetRequiredService<Connection>());

        MyDerivedInterfacePrx? derived = await prx.AsAsync<MyDerivedInterfacePrx>();

        Assert.That(derived, Is.Null);
    }

    private class MyBaseInterface : Service, IMyBaseInterface
    {
    }

    private class MyDerivedInterface : MyBaseInterface, IMyDerivedInterface
    {
    }
}
