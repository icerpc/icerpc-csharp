// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Slice;
using IceRpc.Tests.Common;
using NUnit.Framework;

namespace IceRpc.Tests.Slice.Identifiers;

/// <summary>These tests verify that the cs::identifier attribute will cause slicec-cs to generate C# with the
/// specified identifiers. As such, most of these tests cover trivial things. The purpose is mainly to ensure that the
/// code generation worked correctly. </summary>
[Parallelizable(scope: ParallelScope.All)]
public class IdentifierAttributeTests
{
    [Test]
    public void Renamed_struct_identifier()
    {
        // Act
        REnamedStruct myStruct = new REnamedStruct(1);

        // Assert
        Assert.That(myStruct.renamedX, Is.EqualTo(1));
    }

    [Test]
    public async Task Renamed_interface_and_operation()
    {
        // Arrange
        var invoker = new ColocInvoker(new IdentifierOperationsService());
        var proxy = new REnamedInterfaceProxy(invoker);

        // Act / Assert
        _ = await proxy.REnamedOpAsync(renamedParam: new REnamedStruct(1));
    }

    [Test]
    public void Renamed_exception()
    {
        // Act
        REnamedException ex = new REnamedException();

        // Assert
        Assert.That(typeof(REnamedException).GetSliceTypeId(), Is.EqualTo("::IceRpc::Tests::Slice::OriginalException"));
    }

    [Test]
    public void Renamed_enum_and_enumerators()
    {
        // Act / Assert
        REnamedEnum myEnum = REnamedEnum.REnamedEnumerator;

        // Assert
        Assert.That(myEnum, Is.EqualTo(REnamedEnum.REnamedEnumerator));
    }

    [Test]
    public void Renamed_class_with_renamed_data_member()
    {
        // Act / Assert
        REnamedClass myClass = new REnamedClass(1);

        // Assert
        Assert.That(myClass.renamedX, Is.EqualTo(1));
    }

    private sealed class IdentifierOperationsService : Service, IREnamedInterfaceService
    {
        public ValueTask<(int, int)> REnamedOpAsync(
            REnamedStruct renamedParam,
            IFeatureCollection features,
            CancellationToken cancellationToken) => new((1, 2));
    }
}
