// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Tests.Common;
using NUnit.Framework;

namespace IceRpc.Ice.Generator.Tests;

[Parallelizable(scope: ParallelScope.All)]
public partial class CsIdentifierOnModuleTests
{
    [Test]
    public async Task Operation_with_types_using_cs_identifier_attribute()
    {
        // Arrange
        var invoker = new ColocInvoker(new CsIdentifierOperationsService());
        var proxy = new CsIdentifierOperationsProxy(invoker);

        // Act
        CsIdentifierAttribute.MappedNamespace.S1 r =
            await proxy.Op1Async(new CsIdentifierAttribute.MappedNamespace.S1(10));

        // Assert
        Assert.That(r.I, Is.EqualTo(10));
    }

    [Service]
    private sealed partial class CsIdentifierOperationsService : ICsIdentifierOperationsService
    {
        public ValueTask<CsIdentifierAttribute.MappedNamespace.S1> Op1Async(
            CsIdentifierAttribute.MappedNamespace.S1 p,
            IFeatureCollection features,
            CancellationToken cancellationToken) => new(new CsIdentifierAttribute.MappedNamespace.S1(p.I));
    }
}
