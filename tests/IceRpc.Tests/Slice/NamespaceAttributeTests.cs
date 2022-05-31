// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Slice;
using IceRpc.Tests.Common;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Tests.Slice;

[Parallelizable(scope: ParallelScope.All)]
public class NamespaceAttributeTests
{
    class NamespaceOperations : Service, INamespaceOperations
    {
        public ValueTask<NamespaceAttribute.WithNamespace.N1.N2.S1> Op1Async(
            NamespaceAttribute.M1.M2.M3.S1 p,
            IFeatureCollection features,
            CancellationToken cancel) => new(new NamespaceAttribute.WithNamespace.N1.N2.S1($"{p.I}"));
    }

    [Test]
    public async Task Operation_with_types_using_cs_namespace_attribute()
    {
        await using ServiceProvider provider = new ServiceCollection()
            .AddColocTest(new NamespaceOperations())
            .BuildServiceProvider();

        provider.GetRequiredService<Server>().Listen();
        var prx = NamespaceOperationsPrx.FromConnection(provider.GetRequiredService<ClientConnection>());

        NamespaceAttribute.WithNamespace.N1.N2.S1 r =
            await prx.Op1Async(new NamespaceAttribute.M1.M2.M3.S1(10));

        Assert.That(r.I, Is.EqualTo("10"));
    }
}
