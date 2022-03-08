// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.TypeIdAttributeTests;
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
        [typeof(MyClass)] = "::IceRpc::Slice::TypeIdAttributeTests::MyClass",
        [typeof(IMyInterfacePrx)] = "::IceRpc::Slice::TypeIdAttributeTests::MyInterface",
        [typeof(MyInterfacePrx)] = "::IceRpc::Slice::TypeIdAttributeTests::MyInterface",
        [typeof(IMyInterface)] = "::IceRpc::Slice::TypeIdAttributeTests::MyInterface",
        [typeof(MyException)] = "::IceRpc::Slice::TypeIdAttributeTests::MyException",
        [typeof(MyStruct)] = "::IceRpc::Slice::TypeIdAttributeTests::MyStruct",
        [typeof(Slice.TypeIdAttributeTests.Inner.MyClass)] =
            "::IceRpc::Slice::TypeIdAttributeTests::Inner::myClass",
        [typeof(Slice.TypeIdAttributeTests.Inner.MyInterfacePrx)] =
            "::IceRpc::Slice::TypeIdAttributeTests::Inner::myInterface",
        [typeof(Slice.TypeIdAttributeTests.Inner.IMyInterface)] =
            "::IceRpc::Slice::TypeIdAttributeTests::Inner::myInterface",
        [typeof(Slice.TypeIdAttributeTests.Inner.MyException)] =
            "::IceRpc::Slice::TypeIdAttributeTests::Inner::myException",
        [typeof(Slice.TypeIdAttributeTests.Inner.MyStruct)] =
            "::IceRpc::Slice::TypeIdAttributeTests::Inner::myStruct",

    };

    /// <summary>A collection of types generated from Slice definitions and its expected default path.</summary>
    private static readonly Dictionary<Type, string> _defaultPaths = new()
    {
        [typeof(ServicePrx)] = "/Slice.Service",
        [typeof(MyClass)] = "/IceRpc.Slice.TypeIdAttributeTests.MyClass",
        [typeof(IMyInterfacePrx)] = "/IceRpc.Slice.TypeIdAttributeTests.MyInterface",
        [typeof(MyInterfacePrx)] = "/IceRpc.Slice.TypeIdAttributeTests.MyInterface",
        [typeof(IMyInterface)] = "/IceRpc.Slice.TypeIdAttributeTests.MyInterface",
        [typeof(MyException)] = "/IceRpc.Slice.TypeIdAttributeTests.MyException",
        [typeof(MyStruct)] = "/IceRpc.Slice.TypeIdAttributeTests.MyStruct",
        [typeof(Slice.TypeIdAttributeTests.Inner.MyClass)] =
            "/IceRpc.Slice.TypeIdAttributeTests.Inner.myClass",
        [typeof(Slice.TypeIdAttributeTests.Inner.MyInterfacePrx)] =
            "/IceRpc.Slice.TypeIdAttributeTests.Inner.myInterface",
        [typeof(Slice.TypeIdAttributeTests.Inner.MyException)] =
            "/IceRpc.Slice.TypeIdAttributeTests.Inner.myException",
        [typeof(Slice.TypeIdAttributeTests.Inner.MyStruct)] =
            "/IceRpc.Slice.TypeIdAttributeTests.Inner.myStruct",
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
