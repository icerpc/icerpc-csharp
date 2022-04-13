// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.TypeIdAttributeTestNamespace;
using NUnit.Framework;

namespace IceRpc.Slice.Tests;

public sealed class TypeIdAttributeTests
{
    /// <summary>Provides test case data for <see cref="Get_default_path(Type, string)"/> test.</summary>
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

    /// <summary>Provides test case data for <see cref="Get_typeId(Type, string?)"/> test.</summary>
    private static IEnumerable<TestCaseData> GetSliceTypeIdSource
    {
        get
        {
            foreach ((Type type, string path) in _typeIds)
            {
                yield return new TestCaseData(type, path);
            }
        }
    }

    /// <summary>A collection of types generated from Slice definitions and its expected type IDs.</summary>
    private static readonly Dictionary<Type, string> _typeIds = new()
    {
        [typeof(ServicePrx)] = "::Slice::Service",
        [typeof(MyClass)] = "::IceRpc::Slice::TypeIdAttributeTestNamespace::MyClass",
        [typeof(IMyInterfacePrx)] = "::IceRpc::Slice::TypeIdAttributeTestNamespace::MyInterface",
        [typeof(MyInterfacePrx)] = "::IceRpc::Slice::TypeIdAttributeTestNamespace::MyInterface",
        [typeof(IMyInterface)] = "::IceRpc::Slice::TypeIdAttributeTestNamespace::MyInterface",
        [typeof(TypeIdAttributeTestNamespace.MyException)] =
            "::IceRpc::Slice::TypeIdAttributeTestNamespace::MyException",
        [typeof(TypeIdAttributeTestNamespace.MyStruct)] = "::IceRpc::Slice::TypeIdAttributeTestNamespace::MyStruct",
        [typeof(TypeIdAttributeTestNamespace.Inner.MyClass)] =
            "::IceRpc::Slice::TypeIdAttributeTestNamespace::Inner::myClass",
        [typeof(TypeIdAttributeTestNamespace.Inner.MyInterfacePrx)] =
            "::IceRpc::Slice::TypeIdAttributeTestNamespace::Inner::myInterface",
        [typeof(TypeIdAttributeTestNamespace.Inner.IMyInterface)] =
            "::IceRpc::Slice::TypeIdAttributeTestNamespace::Inner::myInterface",
        [typeof(TypeIdAttributeTestNamespace.Inner.MyException)] =
            "::IceRpc::Slice::TypeIdAttributeTestNamespace::Inner::myException",
        [typeof(TypeIdAttributeTestNamespace.Inner.MyStruct)] =
            "::IceRpc::Slice::TypeIdAttributeTestNamespace::Inner::myStruct",

    };

    /// <summary>A collection of types generated from Slice definitions and its expected default path.</summary>
    private static readonly Dictionary<Type, string> _defaultPaths = new()
    {
        [typeof(ServicePrx)] = "/Slice.Service",
        [typeof(MyClass)] = "/IceRpc.Slice.TypeIdAttributeTestNamespace.MyClass",
        [typeof(IMyInterfacePrx)] = "/IceRpc.Slice.TypeIdAttributeTestNamespace.MyInterface",
        [typeof(MyInterfacePrx)] = "/IceRpc.Slice.TypeIdAttributeTestNamespace.MyInterface",
        [typeof(IMyInterface)] = "/IceRpc.Slice.TypeIdAttributeTestNamespace.MyInterface",
        [typeof(TypeIdAttributeTestNamespace.MyException)] = "/IceRpc.Slice.TypeIdAttributeTestNamespace.MyException",
        [typeof(TypeIdAttributeTestNamespace.MyStruct)] = "/IceRpc.Slice.TypeIdAttributeTestNamespace.MyStruct",
        [typeof(TypeIdAttributeTestNamespace.Inner.MyClass)] =
            "/IceRpc.Slice.TypeIdAttributeTestNamespace.Inner.myClass",
        [typeof(TypeIdAttributeTestNamespace.Inner.MyInterfacePrx)] =
            "/IceRpc.Slice.TypeIdAttributeTestNamespace.Inner.myInterface",
        [typeof(TypeIdAttributeTestNamespace.Inner.MyException)] =
            "/IceRpc.Slice.TypeIdAttributeTestNamespace.Inner.myException",
        [typeof(TypeIdAttributeTestNamespace.Inner.MyStruct)] =
            "/IceRpc.Slice.TypeIdAttributeTestNamespace.Inner.myStruct",
    };

    /// <summary>Verifies that types generated from Slice definitions have the expected type ID.</summary>
    /// <param name="type">The <see cref="Type"/> of the generated type to test.</param>
    /// <param name="expected">The expected type ID.</param>
    [Test, TestCaseSource(nameof(GetSliceTypeIdSource))]
    public void Get_typeId(Type type, string? expected)
    {
        string? typeId = type.GetSliceTypeId();

        Assert.That(typeId, Is.EqualTo(expected));
    }

    /// <summary>Verifies that types generated from Slice definitions have the expected default path.</summary>
    /// <param name="type">The <see cref="Type"/> of the generated type to test.</param>
    /// <param name="expected">The expected type ID.</param>
    [Test, TestCaseSource(nameof(GetDefaultPathSource))]
    public void Get_default_path(Type t, string? expected)
    {
        string defaultPath = t.GetDefaultPath();

        Assert.That(defaultPath, Is.EqualTo(expected));
    }
}
