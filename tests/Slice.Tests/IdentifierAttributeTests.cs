// Copyright (c) ZeroC, Inc.

using NUnit.Framework;

namespace Slice.Tests.Identifiers;

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
        var myStruct = new REnamedStruct(1, REnamedEnum.REnamedEnumerator);

        // Assert
        Assert.That(myStruct.renamedX, Is.EqualTo(1));
    }

    [Test]
    public void Renamed_exception()
    {
        // Act
        _ = new REnamedException();

        // Assert
        Assert.That(typeof(REnamedException).GetSliceTypeId(), Is.EqualTo("::Slice::Tests::OriginalException"));
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
    public void Renamed_class_with_renamed_field()
    {
        // Act / Assert
        var myClass = new REnamedClass(1);

        // Assert
        Assert.That(myClass.renamedX, Is.EqualTo(1));
    }
}
