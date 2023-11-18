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

    [Test]
    public void Class_can_implement_multiple_services()
    {
        // Arrange
        var invoker = new ColocInvoker(new MultipleServices());
        var myFirstWidgetClient = new MyFirstWidgetClient(invoker);
        var mySecondWidgetClient = new MySecondWidgetClient(invoker);

        // Act/Assert
        Assert.That(async () => await myFirstWidgetClient.OpFirstAsync(new Empty()), Throws.Nothing);
        Assert.That(async () => await mySecondWidgetClient.OpSecondAsync(new Empty()), Throws.Nothing);
    }

    [Test]
    public void Base_class_implements_multiple_services()
    {
        // Arrange
        var invoker = new ColocInvoker(new MultipleServices2());
        var myFirstWidgetClient = new MyFirstWidgetClient(invoker);
        var mySecondWidgetClient = new MySecondWidgetClient(invoker);

        // Act/Assert
        Assert.That(async () => await myFirstWidgetClient.OpFirstAsync(new Empty()), Throws.Nothing);
        Assert.That(async () => await mySecondWidgetClient.OpSecondAsync(new Empty()), Throws.Nothing);
    }

    [ProtobufService]
    internal partial class MultipleServices : IMyFirstWidgetService, IMySecondWidgetService
    {
        public ValueTask<Empty> OpFirstAsync(
            Empty message,
            IFeatureCollection? features,
            CancellationToken cancellationToken) => new(message);

        public ValueTask<Empty> OpSecondAsync(
            Empty message,
            IFeatureCollection? features,
            CancellationToken cancellationToken) => new(message);
    }

    internal class BaseImplementsMultipleServices : IMyFirstWidgetService, IMySecondWidgetService
    {
        public ValueTask<Empty> OpFirstAsync(
            Empty message,
            IFeatureCollection? features,
            CancellationToken cancellationToken) => new(message);

        public ValueTask<Empty> OpSecondAsync(
            Empty message,
            IFeatureCollection? features,
            CancellationToken cancellationToken) => new(message);
    }

    [ProtobufService]
    internal partial class MultipleServices2 : BaseImplementsMultipleServices { }

    // Avoid un-instantiated internal classes
    // TODO: we should actually test dispatching to these services
    #pragma warning disable CA1812
    [ProtobufService]
    internal partial class TrivialService { }

    [ProtobufService]
    internal partial class DerivedMultipleServices : MultipleServices { }
    #pragma warning restore CA1812
}
