// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Tests;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Slice.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class NamespaceAttributeTests
{
    class NamespaceOperations : Service, INamespaceOperations
    {
        public ValueTask<NamespaceAttribute.WithNamespace.N1.N2.S1> Op1Async(
            NamespaceAttribute.M1.M2.M3.S1 p,
            Dispatch dispatch,
            CancellationToken cancel) => new(new NamespaceAttribute.WithNamespace.N1.N2.S1(p.I));
    }

    [Test]
    public async Task Operation_with_types_using_cs_namespace_attribute()
    {
        await using var provider = new SliceTestServiceCollection()
            .UseDispatcher(new NamespaceOperations())
            .BuildServiceProvider();
        var prx = NamespaceOperationsPrx.FromConnection(provider.GetRequiredService<Connection>());

        NamespaceAttribute.WithNamespace.N1.N2.S1 r =
            await prx.Op1Async(new NamespaceAttribute.M1.M2.M3.S1(10));

        Assert.That(r.I, Is.EqualTo(10));
    }
}
