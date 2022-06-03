// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Slice;
using IceRpc.Tests.Common;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Tests.Slice;

/// <summary>Test IPrx extension methods other than InvokeAsync.</summary>
[Parallelizable(scope: ParallelScope.All)]
public class PrxTests
{
    [Test]
    public async Task Downcast_prx_with_as_sync_succeeds()
    {
        await using ServiceProvider provider = new ServiceCollection()
            .AddColocTest(new MyDerivedInterface())
            .BuildServiceProvider(validateScopes: true);

        provider.GetRequiredService<Server>().Listen();
        var prx = MyBaseInterfacePrx.FromConnection(provider.GetRequiredService<ClientConnection>());

        MyDerivedInterfacePrx? derived = await prx.AsAsync<MyDerivedInterfacePrx>();

        Assert.That(derived, Is.Not.Null);
    }

    [Test]
    public async Task Downcast_prx_with_as_aync_fails()
    {
        await using ServiceProvider provider = new ServiceCollection()
            .AddColocTest(new MyBaseInterface())
            .BuildServiceProvider(validateScopes: true);

        provider.GetRequiredService<Server>().Listen();
        var prx = MyBaseInterfacePrx.FromConnection(provider.GetRequiredService<ClientConnection>());

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
