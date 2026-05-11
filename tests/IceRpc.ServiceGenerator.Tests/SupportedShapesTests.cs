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
}
