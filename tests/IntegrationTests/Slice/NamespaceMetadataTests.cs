// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.IntegrationTests.NamespaceMD.M1.M2.M3;
using IceRpc.IntegrationTests.NamespaceMD.WithNamespace.N1.N2;
using IceRpc.Slice;
using IceRpc.Tests;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.IntegrationTests.NamespaceMD.WithNamespace;

public class NamespaceMetadataTests
{
    public class NamespaceMDOperations : Service, INamespaceMDOperations
    {
        public ValueTask<S2> GetNestedM0M2M3S2Async(Dispatch dispatch, CancellationToken cancel) =>
            new(new S2(10));
        public ValueTask<C1> GetWithNamespaceC2AsC1Async(Dispatch dispatch, CancellationToken cancel) =>
            new(new C2(10, 20));
        public ValueTask<C2> GetWithNamespaceC2AsC2Async(Dispatch dispatch, CancellationToken cancel) =>
            new(new C2(10, 20));
        public ValueTask<S1> GetWithNamespaceN1N2S1Async(Dispatch dispatch, CancellationToken cancel) =>
            new(new S1(10));

        public ValueTask ThrowWithNamespaceE1Async(Dispatch dispatch, CancellationToken cancel) =>
            throw new E2(10, 20);
        public ValueTask ThrowWithNamespaceE2Async(Dispatch dispatch, CancellationToken cancel) =>
            throw new E2(10, 20);
    }

    [Test]
    public async Task Namespace_metadata_operations()
    {
        await using var provider = new IntegrationTestServiceCollection()
            .UseDispatcher(new NamespaceMDOperations())
            .BuildServiceProvider();

        var prx = NamespaceMDOperationsPrx.FromConnection(provider.GetRequiredService<Connection>(), "/service");

        var r1 = await prx.GetNestedM0M2M3S2Async();
        Assert.That(r1.I, Is.EqualTo(10));

        var r2 = await prx.GetWithNamespaceC2AsC1Async();
        Assert.That(r2, Is.TypeOf<C2>());
        Assert.That(r2, Is.TypeOf<C2>());
        var r3 = (C2)r2;
        Assert.That(r3.I, Is.EqualTo(10));
        Assert.That(r3.L, Is.EqualTo(20));

        r3 = await prx.GetWithNamespaceC2AsC2Async();
        Assert.That(r3.I, Is.EqualTo(10));
        Assert.That(r3.L, Is.EqualTo(20));

        Assert.That(async () => await prx.ThrowWithNamespaceE1Async(), Throws.TypeOf<E2>());
        Assert.That(async () => await prx.ThrowWithNamespaceE2Async(), Throws.TypeOf<E2>());
    }
}
