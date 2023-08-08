// Copyright (c) ZeroC, Inc.

using NUnit.Framework;

namespace ZeroC.Slice.Tests.TypeIdAttributeTestNamespace;

public sealed class TypeIdAttributeTests
{
    /// <summary>Provides test case data for the <see cref="Get_all_slice_type_ids" /> test.</summary>
    private static IEnumerable<TestCaseData> GetAllSliceTypeIdsSource
    {
        get
        {
            yield return new TestCaseData(
                typeof(MyDerivedClass),
                new string[]
                {
                    "::ZeroC::Slice::Tests::TypeIdAttributeTestNamespace::MyDerivedClass",
                    "::ZeroC::Slice::Tests::TypeIdAttributeTestNamespace::MyClass"
                });
        }
    }

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

    [Test, TestCaseSource(nameof(GetAllSliceTypeIdsSource))]
    public void Get_all_slice_type_ids(Type type, string[] expected)
    {
        string[] typeIds = type.GetAllSliceTypeIds();
        Assert.That(typeIds, Is.EqualTo(expected));
    }
}
