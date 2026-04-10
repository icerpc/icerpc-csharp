// Copyright (c) ZeroC, Inc.

using Google.Protobuf.WellKnownTypes;
using IceRpc.Features;
using IceRpc.Tests.Common;
using NUnit.Framework;

namespace IceRpc.ServiceGenerator.Tests;

[Parallelizable(ParallelScope.All)]
public partial class MixedServiceTests
{
    /// <summary>Verifies that a single service class can implement an Ice interface, a Slice interface, and a
    /// Protobuf service, and dispatch operations from all three.</summary>
    [Test]
    public async Task Service_implements_ice_slice_and_protobuf_interfaces()
    {
        // Arrange
        var service = new MixedService();
        var invoker = new ColocInvoker(service);

        var iceProxy = new GreeterProxy(invoker);
        var sliceProxy = new SliceGreeterProxy(invoker);
        var protobufClient = new ProtobufGreeterClient(invoker);

        // Act/Assert
        Assert.That(async () => await iceProxy.OpIceAsync(), Throws.Nothing);
        Assert.That(await iceProxy.OpIceWithArgsAsync("Ice"), Is.EqualTo("Ice"));
        Assert.That(async () => await sliceProxy.OpSliceAsync(), Throws.Nothing);
        Assert.That(await sliceProxy.OpSliceWithArgsAsync("Slice"), Is.EqualTo("Slice"));
        Assert.That(async () => await protobufClient.OpProtoAsync(new Empty()), Throws.Nothing);
        Assert.That(
            (await protobufClient.OpProtoWithArgsAsync(new ProtoMessage { Message = "Proto" })).Message,
            Is.EqualTo("Proto"));
    }

    [Service]
    internal partial class MixedService : IGreeterService, ISliceGreeterService, IProtobufGreeterService
    {
        public ValueTask OpIceAsync(IFeatureCollection features, CancellationToken cancellationToken) => default;

        public ValueTask<string> OpIceWithArgsAsync(
            string message,
            IFeatureCollection features,
            CancellationToken cancellationToken) => new(message);

        public ValueTask OpSliceAsync(IFeatureCollection features, CancellationToken cancellationToken) => default;

        public ValueTask<string> OpSliceWithArgsAsync(
            string message,
            IFeatureCollection features,
            CancellationToken cancellationToken) => new(message);

        public ValueTask<Empty> OpProtoAsync(
            Empty message,
            IFeatureCollection? features,
            CancellationToken cancellationToken) => new(new Empty());

        public ValueTask<ProtoMessage> OpProtoWithArgsAsync(
            ProtoMessage message,
            IFeatureCollection? features,
            CancellationToken cancellationToken) => new(message);
    }
}
