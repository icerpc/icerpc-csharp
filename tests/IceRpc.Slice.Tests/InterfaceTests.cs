// Copyright (c) ZeroC, Inc.

using IceRpc.Slice.Ice;
using NUnit.Framework;
using ZeroC.Slice;

namespace IceRpc.Slice.Tests.InterfaceTests;

public sealed class InterfaceTests
{
    /// <summary>Verifies that service interfaces and proxy structs generated from Slice interfaces have the expected
    /// Slice type ID.</summary>
    /// <param name="type">The <see cref="Type" /> of the generated type to test.</param>
    /// <param name="expected">The expected Slice type ID.</param>
    [TestCase(typeof(IceObjectProxy), "::Ice::Object")]
    [TestCase(typeof(IIceObjectService), "::Ice::Object")]
    [TestCase(typeof(PingableProxy), "::IceRpc::Slice::Tests::Pingable")]
    [TestCase(typeof(IPingableService), "::IceRpc::Slice::Tests::Pingable")]
    [TestCase(typeof(MyInterfaceProxy), "::IceRpc::Slice::Tests::InterfaceTests::MyInterface")]
    [TestCase(typeof(IMyInterfaceService), "::IceRpc::Slice::Tests::InterfaceTests::MyInterface")]
    [TestCase(typeof(MyOtherInterfaceProxy), "::IceRpc::Slice::Tests::InterfaceTests::myOtherInterface")]
    [TestCase(typeof(IMyOtherInterfaceService), "::IceRpc::Slice::Tests::InterfaceTests::myOtherInterface")]
    public void Get_slice_type_id(Type type, string? expected)
    {
        string? typeId = type.GetSliceTypeId();
        Assert.That(typeId, Is.EqualTo(expected));
    }

    /// <summary>Verifies that generated service interfaces have the expected default service path.</summary>
    /// <param name="type">The <see cref="Type" /> of the generated type to test.</param>
    /// <param name="expected">The expected default service path.</param>
    [TestCase(typeof(IIceObjectService), "/Ice.Object")]
    [TestCase(typeof(IPingableService), "/IceRpc.Slice.Tests.Pingable")]
    [TestCase(typeof(IMyInterfaceService), "/IceRpc.Slice.Tests.InterfaceTests.MyInterface")]
    [TestCase(typeof(IMyOtherInterfaceService), "/IceRpc.Slice.Tests.InterfaceTests.myOtherInterface")]
    public void Get_interface_default_service_path(Type type, string? expected)
    {
        string? path = type.GetDefaultServicePath();
        Assert.That(path, Is.EqualTo(expected));
    }

    /// <summary>Verifies that generated proxies have the expected default service path.</summary>
    /// <param name="path">The generated default service path constant.</param>
    /// <param name="expected">The expected default service path.</param>
    [TestCase(IceObjectProxy.DefaultServicePath, "/Ice.Object")]
    [TestCase(PingableProxy.DefaultServicePath, "/IceRpc.Slice.Tests.Pingable")]
    [TestCase(MyInterfaceProxy.DefaultServicePath, "/IceRpc.Slice.Tests.InterfaceTests.MyInterface")]
    [TestCase(MyOtherInterfaceProxy.DefaultServicePath, "/IceRpc.Slice.Tests.InterfaceTests.myOtherInterface")]
    public void Get_proxy_default_service_path(string path, string? expected) =>
        Assert.That(path, Is.EqualTo(expected));
}
