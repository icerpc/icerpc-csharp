// Copyright (c) ZeroC, Inc.

using Google.Protobuf.WellKnownTypes;
using IceRpc.Features;
using IceRpc.Tests.Common;
using NUnit.Framework;

namespace IceRpc.Protobuf.Tests;

[Parallelizable(scope: ParallelScope.All)]
public partial class ServiceTests
{
    /// <summary>Verifies that generated service interfaces have the expected default service path.</summary>
    /// <param name="type">The <see cref="System.Type" /> of the generated type to test.</param>
    /// <param name="expected">The expected default service path.</param>
    [TestCase(typeof(IMyWidgetService), "/icerpc.protobuf.tests.MyWidget")]
    [TestCase(typeof(IMyOtherWidgetService), "/icerpc.protobuf.tests.myOtherWidget")]
    public void Get_interface_default_service_path(System.Type type, string? expected)
    {
        string? path = type.GetDefaultServicePath();
        Assert.That(path, Is.EqualTo(expected));
    }

    /// <summary>Verifies that generated client structs have the expected default service path.</summary>
    /// <param name="path">The generated default service path constant.</param>
    /// <param name="expected">The expected default service path.</param>
    [TestCase(MyWidgetClient.DefaultServicePath, "/icerpc.protobuf.tests.MyWidget")]
    [TestCase(MyOtherWidgetClient.DefaultServicePath, "/icerpc.protobuf.tests.myOtherWidget")]
    public void Get_client_default_service_path(string path, string? expected) =>
        Assert.That(path, Is.EqualTo(expected));
}
