// Copyright (c) ZeroC, Inc.

using NUnit.Framework;

namespace ZeroC.Slice.Tests.TypeIdAttributeTestNamespace;

public sealed class TypeIdAttributeTests
{
    /// <summary>Verifies that types generated from Slice definitions have the expected type ID.</summary>
    /// <param name="type">The <see cref="Type" /> of the generated type to test.</param>
    /// <param name="expected">The expected type ID.</param>
    [TestCase(typeof(MyClass), "::ZeroC::Slice::Tests::TypeIdAttributeTestNamespace::MyClass")]
    [TestCase(typeof(MyOtherClass), "::ZeroC::Slice::Tests::TypeIdAttributeTestNamespace::myOtherClass")]
    [TestCase(typeof(MyStruct), null)] // Slice2 struct
    public void Get_slice_type_id(Type type, string? expected)
    {
        string? typeId = type.GetSliceTypeId();
        Assert.That(typeId, Is.EqualTo(expected));
    }
}
