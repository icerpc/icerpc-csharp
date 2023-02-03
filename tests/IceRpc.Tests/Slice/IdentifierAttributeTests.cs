// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Slice;
using IceRpc.Tests.Common;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Tests.Slice.Identifiers;

/// <summary>These tests verify that the cs::identifier attribute will cause slicec-cs to generate C# with the
/// specified identifiers. As such, most of these tests cover trivial things. The purpose is mainly to ensure that the
/// code generation worked correctly. </summary>
[Parallelizable(scope: ParallelScope.All)]
public class IdentifierAttributeTests
{
    public class IdentifierOperations : Service, IREnamedInterface
    {
        public ValueTask<(int, int)> REnamedOpAsync(
            REnamedStruct renamedParam,
            IFeatureCollection features,
            CancellationToken cancellationToken) => new((1, 2));
    }

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
        await using ServiceProvider provider = new ServiceCollection()
            .AddClientServerColocTest(dispatcher: new IdentifierOperations())
            .AddIceRpcProxy<IREnamedInterfaceProxy, REnamedInterfaceProxy>()
            .BuildServiceProvider(validateScopes: true);

        IREnamedInterfaceProxy proxy = provider.GetRequiredService<IREnamedInterfaceProxy>();
        provider.GetRequiredService<Server>().Listen();

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
}
