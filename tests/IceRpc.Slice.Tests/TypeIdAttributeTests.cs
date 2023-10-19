// Copyright (c) ZeroC, Inc.

using IceRpc.Slice.Ice;
using NUnit.Framework;
using ZeroC.Slice;

namespace IceRpc.Slice.Tests.TypeIdAttributeTestNamespace;

public sealed class TypeIdAttributeTests
{
    /// <summary>Verifies that interface types generated from Slice definitions have the expected type ID.</summary>
    /// <param name="type">The <see cref="Type" /> of the generated type to test.</param>
    /// <param name="expected">The expected type ID.</param>
    [TestCase(typeof(IceObjectProxy), "::Ice::Object")]
    [TestCase(typeof(PingableProxy), "::IceRpc::Slice::Tests::Pingable")]
    [TestCase(typeof(MyInterfaceProxy), "::IceRpc::Slice::Tests::TypeIdAttributeTestNamespace::MyInterface")]
    [TestCase(typeof(IMyInterfaceService), "::IceRpc::Slice::Tests::TypeIdAttributeTestNamespace::MyInterface")]
    [TestCase(typeof(MyOtherInterfaceProxy), "::IceRpc::Slice::Tests::TypeIdAttributeTestNamespace::myOtherInterface")]
    [TestCase(typeof(IMyOtherInterfaceService), "::IceRpc::Slice::Tests::TypeIdAttributeTestNamespace::myOtherInterface")]
    public void Get_slice_type_id(Type type, string? expected)
    {
        string? typeId = type.GetSliceTypeId();
        Assert.That(typeId, Is.EqualTo(expected));
    }
}
