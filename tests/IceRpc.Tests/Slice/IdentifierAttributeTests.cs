// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Slice;
using IceRpc.Tests.Common;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Tests.Slice.Identifiers;

/// <summary>These tests verify that the cs::identifier attribute will cause slicec-cs to generate c sharp with the
/// specified identifiers. As such, most of these tests cover trivial things. The purpose is mainly to ensure that the
/// code generation worked correctly. </summary>
[Parallelizable(scope: ParallelScope.All)]
public class IdentifierAttributeTests
{
    class IdentifierOperations : Service, I__renamed_interface
    {
        public ValueTask<(int, int)> __renamed_opAsync(
            __renamed_struct __renamed_param,
            IFeatureCollection features,
            CancellationToken cancellationToken) => new((1, 2));
    }

    [Test]
    public void Renamed_struct_identifier()
    {
        // Act
        __renamed_struct myStruct = new __renamed_struct(1);

        // Assert
        Assert.That(myStruct.__renamed_x, Is.EqualTo(1));
        Assert.That(__renamed_struct.SliceTypeId, Is.EqualTo("::IceRpc::Tests::Slice::OriginalStruct"));
    }

    [Test]
    public async Task Renamed_interface_and_operation()
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddColocTest(new IdentifierOperations())
            .AddIceRpcProxy<I__renamed_interfaceProxy, __renamed_interfaceProxy>()
            .BuildServiceProvider(validateScopes: true);

        I__renamed_interfaceProxy proxy = provider.GetRequiredService<I__renamed_interfaceProxy>();
        provider.GetRequiredService<Server>().Listen();

        // Act / Assert
        _ = await proxy.__renamed_opAsync(new __renamed_struct(1));
    }

    [Test]
    public void Renamed_exception()
    {
        // Act
        __renamed_exception ex = new __renamed_exception();

        // Assert
        Assert.That(__renamed_exception.SliceTypeId, Is.EqualTo("::IceRpc::Tests::Slice::OriginalException"));
    }

    [Test]
    public void Renamed_enum_and_enumerators()
    {
        // Act / Assert
        __renamed_enum myEnum = __renamed_enum.__renamed_enumerator;

        // Assert
        Assert.That(myEnum, Is.EqualTo(__renamed_enum.__renamed_enumerator));
    }

    [Test]
    public void Renamed_class_with_renamed_data_member()
    {
        // Act / Assert
        __renamed_class myClass = new __renamed_class(1);

        // Assert
        Assert.That(myClass.__renamed_x, Is.EqualTo(1));
    }
}
