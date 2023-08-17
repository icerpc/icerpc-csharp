// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Tests.Common;
using NUnit.Framework;

namespace IceRpc.Slice.Tests.Identifiers;

/// <summary>These tests verify that the cs::identifier attribute will cause slicec-cs to generate C# with the
/// specified identifiers. As such, most of these tests cover trivial things. The purpose is mainly to ensure that the
/// code generation worked correctly. </summary>
[Parallelizable(scope: ParallelScope.All)]
public partial class IdentifierAttributeTests
{
    [Test]
    public async Task Renamed_interface_and_operation()
    {
        // Arrange
        var invoker = new ColocInvoker(new IdentifierOperationsService());
        var proxy = new REnamedInterfaceProxy(invoker);

        // Act / Assert
        _ = await proxy.REnamedOpAsync(renamedParam: 1);
    }

    [SliceService]
    private sealed partial class IdentifierOperationsService : IREnamedInterfaceService
    {
        public ValueTask<(int, int)> REnamedOpAsync(
            int renamedParam,
            IFeatureCollection features,
            CancellationToken cancellationToken) => new((1, 2));
    }
}
