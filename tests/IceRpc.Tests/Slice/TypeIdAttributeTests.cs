// Copyright (c) ZeroC, Inc.

using IceRpc.Ice;
using IceRpc.Slice;
using NUnit.Framework;

namespace IceRpc.Tests.Slice.TypeIdAttributeTestNamespace;

public sealed class TypeIdAttributeTests
{
    /// <summary>Provides test case data for the <see cref="Get_all_slice_type_ids" /> test.</summary>
    private static IEnumerable<TestCaseData> GetAllSliceTypeIdsSource
    {
        get
        {
            foreach ((Type type, string[] expected) in _allTypeIds)
            {
                yield return new TestCaseData(type, expected);
            }
        }
    }

    private static readonly Dictionary<Type, string[]> _allTypeIds = new()
    {
        [typeof(IceObjectProxy)] = new string[] { "::Ice::Object" },
        [typeof(PingableProxy)] = new string[] { "::IceRpc::Tests::Slice::Pingable" },
        [typeof(MyDerivedClass)] = new string[]
        {
            "::IceRpc::Tests::Slice::TypeIdAttributeTestNamespace::MyDerivedClass",
            "::IceRpc::Tests::Slice::TypeIdAttributeTestNamespace::MyClass"
        },
        [typeof(IMyDerivedInterface)] = new string[]
        {
            "::IceRpc::Tests::Slice::TypeIdAttributeTestNamespace::Inner::myInterface",
            "::IceRpc::Tests::Slice::TypeIdAttributeTestNamespace::MyDerivedInterface",
            "::IceRpc::Tests::Slice::TypeIdAttributeTestNamespace::MyInterface",
        },
        [typeof(ServerAddress)] = Array.Empty<string>(),
        [typeof(ServiceAddress)] = Array.Empty<string>(),
    };

    /// <summary>Verifies that types generated from Slice definitions have the expected type ID.</summary>
    /// <param name="type">The <see cref="Type" /> of the generated type to test.</param>
    /// <param name="expected">The expected type ID.</param>
    [TestCase(typeof(IceObjectProxy), "::Ice::Object")]
    [TestCase(typeof(PingableProxy), "::IceRpc::Tests::Slice::Pingable")]
    [TestCase(typeof(MyClass), "::IceRpc::Tests::Slice::TypeIdAttributeTestNamespace::MyClass")]
    [TestCase(typeof(IMyInterface), "::IceRpc::Tests::Slice::TypeIdAttributeTestNamespace::MyInterface")]
    [TestCase(typeof(MyInterfaceProxy), "::IceRpc::Tests::Slice::TypeIdAttributeTestNamespace::MyInterface")]
    [TestCase(typeof(IMyInterfaceService), "::IceRpc::Tests::Slice::TypeIdAttributeTestNamespace::MyInterface")]
    [TestCase(typeof(MyException), null)] // Slice2 exception
    [TestCase(typeof(Inner.MyClass), "::IceRpc::Tests::Slice::TypeIdAttributeTestNamespace::Inner::myClass")]
    [TestCase(typeof(Inner.MyInterfaceProxy), "::IceRpc::Tests::Slice::TypeIdAttributeTestNamespace::Inner::myInterface")]
    [TestCase(typeof(Inner.IMyInterfaceService), "::IceRpc::Tests::Slice::TypeIdAttributeTestNamespace::Inner::myInterface")]
    [TestCase(typeof(Inner.MyException), null)] // Slice2 exception
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

    /// <summary>Verifies that types generated from Slice definitions have the expected default path.</summary>
    /// <param name="type">The <see cref="Type" /> of the generated type to test.</param>
    /// <param name="expected">The expected type ID.</param>
    [TestCase(typeof(IceObjectProxy), "/Ice.Object")]
    [TestCase(typeof(PingableProxy), "/IceRpc.Tests.Slice.Pingable")]
    [TestCase(typeof(IMyInterface), "/IceRpc.Tests.Slice.TypeIdAttributeTestNamespace.MyInterface")]
    [TestCase(typeof(MyInterfaceProxy), "/IceRpc.Tests.Slice.TypeIdAttributeTestNamespace.MyInterface")]
    [TestCase(typeof(IMyInterfaceService), "/IceRpc.Tests.Slice.TypeIdAttributeTestNamespace.MyInterface")]
    [TestCase(typeof(Inner.MyInterfaceProxy), "/IceRpc.Tests.Slice.TypeIdAttributeTestNamespace.Inner.myInterface")]
    public void Get_default_path(Type type, string expected)
    {
        string defaultPath = type.GetDefaultServicePath();
        Assert.That(defaultPath, Is.EqualTo(expected));
    }

    [TestCase(typeof(MyClass))]
    [TestCase(typeof(MyException))]
    [TestCase(typeof(ServerAddress))]
    public void Get_default_path_exception(Type type) =>
        Assert.That(() => type.GetDefaultServicePath(), Throws.ArgumentException);
}
