// Copyright (c) ZeroC, Inc.

using NUnit.Framework;
using IceRpc.Ice.Codec;

namespace IceRpc.Ice.Generator.Base.Tests.Identifiers;

/// <summary>These tests verify that the cs::identifier attribute will cause the Ice compiler to generate C#
/// with the specified identifiers.</summary>
[Parallelizable(scope: ParallelScope.All)]
public class IdentifierAttributeTests
{
    [Test]
    public void Renamed_exception()
    {
        // Act
        _ = new REnamedException();

        // Assert
        Assert.That(typeof(REnamedException).GetIceTypeId(), Is.EqualTo("::IceRpc::Ice::Generator::Base::Tests::OriginalException"));
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
