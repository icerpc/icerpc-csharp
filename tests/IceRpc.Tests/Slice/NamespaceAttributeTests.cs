// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Slice;
using IceRpc.Tests.Common;
using NUnit.Framework;

namespace IceRpc.Tests.Slice;

[Parallelizable(scope: ParallelScope.All)]
public class NamespaceAttributeTests
{
    [Test]
    public async Task Operation_with_types_using_cs_namespace_attribute()
    {
        // Arrange
        var invoker = new ColocInvoker(new NamespaceOperationsService());
        var proxy = new NamespaceOperationsProxy(invoker);

        // Act
        NamespaceAttribute.MappedNamespace.S1 r =
            await proxy.Op1Async(new NamespaceAttribute.MappedNamespace.S1(10));

        // Assert
        Assert.That(r.I, Is.EqualTo(10));
    }

    private sealed class NamespaceOperationsService : Service, INamespaceOperationsService
    {
        public ValueTask<NamespaceAttribute.MappedNamespace.S1> Op1Async(
            NamespaceAttribute.MappedNamespace.S1 p,
            IFeatureCollection features,
            CancellationToken cancellationToken) => new(new NamespaceAttribute.MappedNamespace.S1(p.I));
    }
}
