// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Tests.Common;
using NUnit.Framework;

namespace IceRpc.ServiceGenerator.Tests;

/// <summary>Tests that verify the service generator accepts the various supported C# type shapes for a service
/// declaration.</summary>
[Parallelizable(ParallelScope.All)]
public partial class SupportedShapesTests
{
    /// <summary>Verifies that the service generator accepts a record class as a service type and dispatches
    /// operations correctly.</summary>
    [Test]
    public async Task Service_can_be_a_record_class()
    {
        // Arrange
        var service = new RecordService();
        var invoker = new ColocInvoker(service);

        var sliceProxy = new SliceGreeterProxy(invoker);

        // Act/Assert
        Assert.That(async () => await sliceProxy.OpSliceAsync(), Throws.Nothing);
        Assert.That(await sliceProxy.OpSliceWithArgsAsync("Record"), Is.EqualTo("Record"));
    }

    /// <summary>Verifies that the service generator accepts a generic class as a service type and dispatches
    /// operations correctly. The generated partial class must include the type-parameter list to merge with the
    /// user-written declaration.</summary>
    [Test]
    public async Task Service_can_be_a_generic_class()
    {
        // Arrange
        var service = new GenericService<string>();
        var invoker = new ColocInvoker(service);

        var sliceProxy = new SliceGreeterProxy(invoker);

        // Act/Assert
        Assert.That(async () => await sliceProxy.OpSliceAsync(), Throws.Nothing);
        Assert.That(await sliceProxy.OpSliceWithArgsAsync("Generic"), Is.EqualTo("Generic"));
    }

    /// <summary>Verifies that the service generator correctly emits all enclosing types when the service is nested
    /// more than one level deep, including when an enclosing type is generic.</summary>
    [Test]
    public async Task Service_can_be_nested_multiple_levels_deep()
    {
        // Arrange
        var service = new MultiLevelOuter<int>.MultiLevelMiddle.MultiLevelLeaf();
        var invoker = new ColocInvoker(service);

        var sliceProxy = new SliceGreeterProxy(invoker);

        // Act/Assert
        Assert.That(async () => await sliceProxy.OpSliceAsync(), Throws.Nothing);
        Assert.That(await sliceProxy.OpSliceWithArgsAsync("Nested"), Is.EqualTo("Nested"));
    }

    /// <summary>Verifies that the service generator correctly emits the enclosing type when the service is nested
    /// inside a record struct.</summary>
    [Test]
    public async Task Service_can_be_nested_in_a_record_struct()
    {
        var service = new RecordStructOuter.RecordStructLeaf();
        var invoker = new ColocInvoker(service);

        var sliceProxy = new SliceGreeterProxy(invoker);

        Assert.That(async () => await sliceProxy.OpSliceAsync(), Throws.Nothing);
        Assert.That(await sliceProxy.OpSliceWithArgsAsync("RecordStruct"), Is.EqualTo("RecordStruct"));
    }

    /// <summary>Verifies that the service generator correctly emits the enclosing type when the service is nested
    /// inside an interface.</summary>
    [Test]
    public async Task Service_can_be_nested_in_an_interface()
    {
        var service = new IInterfaceOuter.InterfaceLeaf();
        var invoker = new ColocInvoker(service);

        var sliceProxy = new SliceGreeterProxy(invoker);

        Assert.That(async () => await sliceProxy.OpSliceAsync(), Throws.Nothing);
        Assert.That(await sliceProxy.OpSliceWithArgsAsync("Interface"), Is.EqualTo("Interface"));
    }

    [Service]
    internal partial record class RecordService : ISliceGreeterService
    {
        public ValueTask OpSliceAsync(IFeatureCollection features, CancellationToken cancellationToken) => default;

        public ValueTask<string> OpSliceWithArgsAsync(
            string message,
            IFeatureCollection features,
            CancellationToken cancellationToken) => new(message);
    }

    [Service]
    internal partial class GenericService<T> : ISliceGreeterService where T : class
    {
        public ValueTask OpSliceAsync(IFeatureCollection features, CancellationToken cancellationToken) => default;

        public ValueTask<string> OpSliceWithArgsAsync(
            string message,
            IFeatureCollection features,
            CancellationToken cancellationToken) => new(message);
    }

    internal partial class MultiLevelOuter<TOuter>
    {
        internal partial class MultiLevelMiddle
        {
            [Service]
            internal partial class MultiLevelLeaf : ISliceGreeterService
            {
                public ValueTask OpSliceAsync(IFeatureCollection features, CancellationToken cancellationToken) =>
                    default;

                public ValueTask<string> OpSliceWithArgsAsync(
                    string message,
                    IFeatureCollection features,
                    CancellationToken cancellationToken) => new(message);
            }
        }
    }

    internal partial record struct RecordStructOuter
    {
        [Service]
        internal partial class RecordStructLeaf : ISliceGreeterService
        {
            public ValueTask OpSliceAsync(IFeatureCollection features, CancellationToken cancellationToken) => default;

            public ValueTask<string> OpSliceWithArgsAsync(
                string message,
                IFeatureCollection features,
                CancellationToken cancellationToken) => new(message);
        }
    }

    internal partial interface IInterfaceOuter
    {
        [Service]
        internal partial class InterfaceLeaf : ISliceGreeterService
        {
            public ValueTask OpSliceAsync(IFeatureCollection features, CancellationToken cancellationToken) => default;

            public ValueTask<string> OpSliceWithArgsAsync(
                string message,
                IFeatureCollection features,
                CancellationToken cancellationToken) => new(message);
        }
    }
}
