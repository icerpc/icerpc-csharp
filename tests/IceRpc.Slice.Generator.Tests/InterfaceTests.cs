// Copyright (c) ZeroC, Inc.

using NUnit.Framework;

namespace IceRpc.Slice.Generator.Tests;

[Parallelizable(scope: ParallelScope.All)]
public sealed class InterfaceTests
{
    /// <summary>Verifies that generated service interfaces have the expected default service path.</summary>
    /// <param name="type">The <see cref="Type" /> of the generated type to test.</param>
    /// <param name="expected">The expected default service path.</param>
    [TestCase(typeof(IPingableService), "/IceRpc.Slice.Generator.Tests.Pingable")]
    [TestCase(typeof(IMyWidgetService), "/IceRpc.Slice.Generator.Tests.MyWidget")]
    [TestCase(typeof(MyBaseService), "/IceRpc.Slice.Generator.Tests.Base")]
    [TestCase(typeof(IMyOtherWidgetService), "/IceRpc.Slice.Generator.Tests.myOtherWidget")]
    public void Get_interface_default_service_path(Type type, string? expected)
    {
        string? path = type.GetDefaultServicePath();
        Assert.That(path, Is.EqualTo(expected));
    }

    /// <summary>Verifies that generated proxies have the expected default service path.</summary>
    /// <param name="path">The generated default service path constant.</param>
    /// <param name="expected">The expected default service path.</param>
    [TestCase(PingableProxy.DefaultServicePath, "/IceRpc.Slice.Generator.Tests.Pingable")]
    [TestCase(MyWidgetProxy.DefaultServicePath, "/IceRpc.Slice.Generator.Tests.MyWidget")]
    [TestCase(MyOtherWidgetProxy.DefaultServicePath, "/IceRpc.Slice.Generator.Tests.myOtherWidget")]
    public void Get_proxy_default_service_path(string path, string? expected) =>
        Assert.That(path, Is.EqualTo(expected));

    [Test]
    public void Get_class_default_service_path_throws_if_multiple_service_paths() =>
        Assert.That(
            () => typeof(MyMoreDerivedService).GetDefaultServicePath(),
            Throws.InstanceOf<ArgumentException>());

    [Test]
    public void Router_map_with_a_service_class_that_implements_multiple_services_throws() =>
        Assert.That(
            () => new Router().Map(new MyMoreDerivedService()),
            Throws.InstanceOf<ArgumentException>());

    [Test]
    public void Router_map_with_an_interface_succeeds() =>
        Assert.That(
            () => new Router().Map<MyBaseService>(new MyMoreDerivedService()),
            Throws.Nothing);
}
