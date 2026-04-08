// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Tests.Common;
using NUnit.Framework;

namespace IceRpc.Slice.Generator.Tests;

[Parallelizable(scope: ParallelScope.All)]
public partial class ModuleIdentifierTests
{
    [Test]
    public async Task Operation_with_types_from_module_with_cs_identifier()
    {
        // Arrange
        var invoker = new ColocInvoker(new IdentifierOperationsService());
        var proxy = new IdentifierOperationsProxy(invoker);

        // Act
        WithIdentifier.MappedNamespace.S1 r =
            await proxy.Op1Async(new WithIdentifier.MappedNamespace.S1(10));

        // Assert
        Assert.That(r.I, Is.EqualTo(10));
    }

    [Service]
    private sealed partial class IdentifierOperationsService : IIdentifierOperationsService
    {
        public ValueTask<WithIdentifier.MappedNamespace.S1> Op1Async(
            WithIdentifier.MappedNamespace.S1 p,
            IFeatureCollection features,
            CancellationToken cancellationToken) => new(new WithIdentifier.MappedNamespace.S1(p.I));
    }
}
