// Copyright (c) ZeroC, Inc.

using NUnit.Framework;
using ZeroC.Slice.Codec;

namespace IceRpc.Slice.CodeGen.Tests;

[Parallelizable(scope: ParallelScope.All)]
public sealed class InterfaceTests
{
    /// <summary>Verifies that generated service interfaces have the expected default service path.</summary>
    /// <param name="type">The <see cref="Type" /> of the generated type to test.</param>
    /// <param name="expected">The expected default service path.</param>
    [TestCase(typeof(IPingableService), "/IceRpc.Slice.CodeGen.Tests.Pingable")]
    [TestCase(typeof(IMyWidgetService), "/IceRpc.Slice.CodeGen.Tests.MyWidget")]
    [TestCase(typeof(IMyOtherWidgetService), "/IceRpc.Slice.CodeGen.Tests.myOtherWidget")]
    public void Get_interface_default_service_path(Type type, string? expected)
    {
        string? path = type.GetDefaultServicePath();
        Assert.That(path, Is.EqualTo(expected));
    }

    /// <summary>Verifies that generated proxies have the expected default service path.</summary>
    /// <param name="path">The generated default service path constant.</param>
    /// <param name="expected">The expected default service path.</param>
    [TestCase(PingableProxy.DefaultServicePath, "/IceRpc.Slice.CodeGen.Tests.Pingable")]
    [TestCase(MyWidgetProxy.DefaultServicePath, "/IceRpc.Slice.CodeGen.Tests.MyWidget")]
    [TestCase(MyOtherWidgetProxy.DefaultServicePath, "/IceRpc.Slice.CodeGen.Tests.myOtherWidget")]
    public void Get_proxy_default_service_path(string path, string? expected) =>
        Assert.That(path, Is.EqualTo(expected));
}
