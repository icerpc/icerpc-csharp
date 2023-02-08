// Copyright (c) ZeroC, Inc.

using IceRpc.Slice;
using NUnit.Framework;

namespace IceRpc.Tests.Slice.TypeIdAttributeTestNamespace;

public sealed class TypeIdAttributeTests
{
    /// <summary>Provides test case data for <see cref="Get_default_path(Type, string)" /> test.</summary>
    private static IEnumerable<TestCaseData> GetDefaultPathSource
    {
        get
        {
            foreach ((Type type, string path) in _defaultPaths)
            {
                yield return new TestCaseData(type, path);
            }
        }
    }

    /// <summary>Provides test case data for <see cref="Get_typeId(Type, string?)" /> test.</summary>
    private static IEnumerable<TestCaseData> GetSliceTypeIdSource
    {
        get
        {
            foreach ((Type type, string? path) in _typeIds)
            {
                yield return new TestCaseData(type, path);
            }
        }
    }

    /// <summary>A collection of types generated from Slice definitions and its expected type IDs.</summary>
    private static readonly Dictionary<Type, string?> _typeIds = new()
    {
        [typeof(ServiceProxy)] = "::IceRpc::Slice::Service",
        [typeof(MyClass)] = "::IceRpc::Tests::Slice::TypeIdAttributeTestNamespace::MyClass",
        [typeof(IMyInterface)] = "::IceRpc::Tests::Slice::TypeIdAttributeTestNamespace::MyInterface",
        [typeof(MyInterfaceProxy)] = "::IceRpc::Tests::Slice::TypeIdAttributeTestNamespace::MyInterface",
        [typeof(IMyInterfaceService)] = "::IceRpc::Tests::Slice::TypeIdAttributeTestNamespace::MyInterface",
        [typeof(MyException)] = null, // Slice2 exception
        [typeof(Inner.MyClass)] = "::IceRpc::Tests::Slice::TypeIdAttributeTestNamespace::Inner::myClass",
        [typeof(Inner.MyInterfaceProxy)] = "::IceRpc::Tests::Slice::TypeIdAttributeTestNamespace::Inner::myInterface",
        [typeof(Inner.IMyInterfaceService)] = "::IceRpc::Tests::Slice::TypeIdAttributeTestNamespace::Inner::myInterface",
        [typeof(Inner.MyException)] = null, // Slice2 exception
    };

    /// <summary>A collection of types generated from Slice definitions and its expected default path.</summary>
    private static readonly Dictionary<Type, string> _defaultPaths = new()
    {
        [typeof(ServiceProxy)] = "/IceRpc.Slice.Service",
        [typeof(MyClass)] = "/IceRpc.Tests.Slice.TypeIdAttributeTestNamespace.MyClass",
        [typeof(IMyInterface)] = "/IceRpc.Tests.Slice.TypeIdAttributeTestNamespace.MyInterface",
        [typeof(MyInterfaceProxy)] = "/IceRpc.Tests.Slice.TypeIdAttributeTestNamespace.MyInterface",
        [typeof(IMyInterfaceService)] = "/IceRpc.Tests.Slice.TypeIdAttributeTestNamespace.MyInterface",
        [typeof(Inner.MyClass)] = "/IceRpc.Tests.Slice.TypeIdAttributeTestNamespace.Inner.myClass",
        [typeof(Inner.MyInterfaceProxy)] = "/IceRpc.Tests.Slice.TypeIdAttributeTestNamespace.Inner.myInterface",
    };

    /// <summary>Verifies that types generated from Slice definitions have the expected type ID.</summary>
    /// <param name="type">The <see cref="Type" /> of the generated type to test.</param>
    /// <param name="expected">The expected type ID.</param>
    [Test, TestCaseSource(nameof(GetSliceTypeIdSource))]
    public void Get_typeId(Type type, string? expected)
    {
        string? typeId = type.GetSliceTypeId();

        Assert.That(typeId, Is.EqualTo(expected));
    }

    /// <summary>Verifies that types generated from Slice definitions have the expected default path.</summary>
    /// <param name="type">The <see cref="Type" /> of the generated type to test.</param>
    /// <param name="expected">The expected type ID.</param>
    [Test, TestCaseSource(nameof(GetDefaultPathSource))]
    public void Get_default_path(Type type, string? expected)
    {
        string defaultPath = type.GetDefaultPath();

        Assert.That(defaultPath, Is.EqualTo(expected));
    }
}
