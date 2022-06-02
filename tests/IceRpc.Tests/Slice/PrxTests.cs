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

    [Test]
    public async Task Prx_tagged_default_values()
    {
        var service = new MyTaggedOperations();
        await using ServiceProvider provider = new ServiceCollection()
            .AddColocTest(service)
            .BuildServiceProvider(validateScopes: true);

        provider.GetRequiredService<Server>().Listen();
        var prx = MyTaggedOperationsPrx.FromConnection(provider.GetRequiredService<ClientConnection>());

        await prx.OpAsync(1, z: 10);

        Assert.That(service.X, Is.EqualTo(1));
        Assert.That(service.Y, Is.Null);
        Assert.That(service.Z, Is.EqualTo(10));
    }

    [Test]
    public async Task Prx_tagged_default_values_with_readonly_memory_params()
    {
        var service = new MyTaggedOperationsReadOnlyMemoryParams();
        await using ServiceProvider provider = new ServiceCollection()
            .AddColocTest(service)
            .BuildServiceProvider(validateScopes: true);

        provider.GetRequiredService<Server>().Listen();
        var prx = MyTaggedOperationsReadOnlyMemoryParamsPrx.FromConnection(provider.GetRequiredService<ClientConnection>());

        await prx.OpAsync(new int[] { 1 }, z: new int[] { 10 });

        Assert.That(service.X, Is.EqualTo(new int[] { 1 }));
        Assert.That(service.Y, Is.Null);
        Assert.That(service.Z, Is.EqualTo(new int[] { 10 }));
    }

    private class MyBaseInterface : Service, IMyBaseInterface
    {
    }

    private class MyDerivedInterface : MyBaseInterface, IMyDerivedInterface
    {
    }

    private class MyTaggedOperations : Service, IMyTaggedOperations
    {
        internal int X { get; set; }
        internal int? Y { get; set; }
        internal int? Z { get; set; }

        public ValueTask OpAsync(int x, int? y, int? z, IFeatureCollection features, CancellationToken cancel)
        {
            X = x;
            Y = y;
            Z = z;
            return default;
        }
    }

    private class MyTaggedOperationsReadOnlyMemoryParams : Service, IMyTaggedOperationsReadOnlyMemoryParams
    {
        internal int[] X { get; set; } = Array.Empty<int>();
        internal int[]? Y { get; set; }
        internal int[]? Z { get; set; }

        public ValueTask OpAsync(int[] x, int[]? y, int[]? z, IFeatureCollection features, CancellationToken cancel)
        {
            X = x;
            Y = y;
            Z = z;
            return default;
        }
    }
}
