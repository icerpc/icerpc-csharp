// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Ice.Derived.Generator.Tests;
using IceRpc.Tests.Common;
using NUnit.Framework;
using System.IO.Pipelines;

namespace IceRpc.Ice.Generator.Tests;

[Parallelizable(scope: ParallelScope.All)]
public partial class OperationTests
{
    [Test]
    public void Operation_without_parameters_and_void_return()
    {
        // Arrange
        var invoker = new ColocInvoker(new MyOperationsAService());
        var proxy = new MyOperationsAProxy(invoker);

        // Act/Assert
        Assert.That(async () => await proxy.OpWithoutParametersAndVoidReturnAsync(), Throws.Nothing);
    }

    [Test]
    public void Operation_without_parameters_and_void_return_on_derived_interface()
    {
        // Arrange
        var invoker = new ColocInvoker(new MyDerivedOperationsAService());
        var proxy = new MyDerivedOperationsAProxy(invoker);

        // Act/Assert
        Assert.That(async () => await proxy.OpDerivedWithoutParametersAndVoidReturnAsync(), Throws.Nothing);
    }

    [Test]
    public void Operation_from_base_class()
    {
        // Arrange
        var invoker = new ColocInvoker(new MyDerivedOperationsAService());
        var proxy = new MyDerivedOperationsAProxy(invoker);

        // Act/Assert
        Assert.That(async () => await proxy.OpWithoutParametersAndVoidReturnAsync(), Throws.Nothing);
    }

    [Test]
    public async Task Operation_with_single_parameter_and_return_value()
    {
        // Arrange
        var invoker = new ColocInvoker(new MyOperationsAService());
        var proxy = new MyOperationsAProxy(invoker);

        int r = await proxy.OpWithSingleParameterAndReturnValueAsync(10);

        Assert.That(r, Is.EqualTo(10));
    }

    [Test]
    public async Task Operation_with_single_parameter_and_return_value_on_derived_interface()
    {
        // Arrange
        var invoker = new ColocInvoker(new MyDerivedOperationsAService());
        var proxy = new MyDerivedOperationsAProxy(invoker);

        int r = await proxy.OpDerivedWithSingleParameterAndReturnValueAsync(10);

        Assert.That(r, Is.EqualTo(10));
    }

    [Test]
    public async Task Operation_with_multiple_parameters_and_return_values()
    {
        // Arrange
        var invoker = new ColocInvoker(new MyOperationsAService());
        var proxy = new MyOperationsAProxy(invoker);

        (int r1, int r2) = await proxy.OpWithMultipleParametersAndReturnValuesAsync(10, 20);

        Assert.That(r1, Is.EqualTo(10));
        Assert.That(r2, Is.EqualTo(20));
    }

    [Test]
    public void Operation_with_special_parameter_names()
    {
        // Arrange
        var invoker = new ColocInvoker(new MyOperationsAService());
        var proxy = new MyOperationsAProxy(invoker);

        // Act/Assert
        Assert.That(
            async () => await proxy.OpWithSpecialParameterNamesAsync(
                cancel: 1,
                features: 2),
            Throws.Nothing);
    }

    [Test]
    public async Task Operation_with_single_return_value_and_marshaled_result_attribute()
    {
        // Arrange
        var invoker = new ColocInvoker(new MyOperationsAService());
        var proxy = new MyOperationsAProxy(invoker);

        // Act
        var r = await proxy.OpWithSingleReturnValueAndMarshaledResultAttributeAsync();

        // Assert
        Assert.That(r, Is.EqualTo(new int[] { 1, 2, 3 }));
    }

    [Test]
    public async Task Operation_with_multiple_return_values_and_marshaled_result_attribute()
    {
        // Arrange
        var invoker = new ColocInvoker(new MyOperationsAService());
        var proxy = new MyOperationsAProxy(invoker);

        // Act
        (int[] r1, int[] r2) = await proxy.OpWithMultipleReturnValuesAndMarshaledResultAttributeAsync();

        // Assert
        Assert.That(r1, Is.EqualTo(new int[] { 1, 2, 3 }));
        Assert.That(r2, Is.EqualTo(new int[] { 1, 2, 3 }));
    }

    /// <summary>Verifies that sequence of fixed size numeric values outgoing parameter is mapped to
    /// <see cref="ReadOnlyMemory{T}" /> the mapping for the incoming parameter is not affected.</summary>
    [Test]
    public void Ice_operation_encode_with_readonly_memory_param()
    {
        // Arrange
        var readOnlyMemory = new ReadOnlyMemory<int>(new int[] { 1, 2, 3 });

        // Act
        PipeReader payload = MyOperationsAProxy.Request.EncodeOpReadOnlyMemory(readOnlyMemory);

        // Assert
        Assert.That(
            async () => await IMyOperationsAService.Request.DecodeOpReadOnlyMemoryAsync(
                new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
                {
                    Payload = payload
                },
                default),
            Is.EqualTo(new int[] { 1, 2, 3 }));
    }

    /// <summary>Verifies that sequence of fixed size numeric values outgoing return value is mapped to
    /// <see cref="ReadOnlyMemory{T}" /> the mapping for the incoming return value is not affected.</summary>
    [Test]
    public void Ice_operation_encode_with_readonly_memory_return()
    {
        // Arrange
        var readOnlyMemory = new ReadOnlyMemory<int>(new int[] { 1, 2, 3 });

        // Act
        PipeReader payload = IMyOperationsAService.Response.EncodeOpReadOnlyMemory(readOnlyMemory);

        // Assert
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = payload
        };
        Assert.That(
            async () => await MyOperationsAProxy.Response.DecodeOpReadOnlyMemoryAsync(
                response,
                request,
                InvalidProxy.Instance,
                default),
            Is.EqualTo(new int[] { 1, 2, 3 }));
    }

    /// <summary>Verifies that an optional sequence of fixed size numeric values outgoing tagged parameter is mapped to
    /// a <see cref="ReadOnlyMemory{T}" /> the mapping for the incoming parameter is not affected.</summary>
    [Test]
    public void Ice_operation_encode_with_readonly_memory_tagged_param(
        [Values(new int[] { 1, 2, 3 }, null)] int[]? p)
    {
        // Arrange
        PipeReader payload = MyOperationsAProxy.Request.EncodeOpReadOnlyMemoryTagged(new ReadOnlyMemory<int>(p));

        // Act/Assert
        Assert.That(
            async () => await IMyOperationsAService.Request.DecodeOpReadOnlyMemoryTaggedAsync(
                new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
                {
                    Payload = payload
                },
                default),
            Is.EqualTo(p));
    }

    /// <summary>Verifies that sequence of fixed size numeric values outgoing tagged return value is mapped to
    /// <see cref="ReadOnlyMemory{T}" /> the mapping for the optional incoming return value is not affected.</summary>
    [Test]
    public void Ice_operation_encode_with_readonly_memory_tagged_return(
        [Values(new int[] { 1, 2, 3 }, null)] int[]? p)
    {
        // Arrange
        var readOnlyMemory = new ReadOnlyMemory<int>(p);

        // Act
        PipeReader payload = IMyOperationsAService.Response.EncodeOpReadOnlyMemoryTagged(readOnlyMemory);

        // Assert
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = payload
        };
        Assert.That(
            async () => await MyOperationsAProxy.Response.DecodeOpReadOnlyMemoryTaggedAsync(
                response,
                request,
                InvalidProxy.Instance,
                default),
            Is.EqualTo(p));
    }

    /// <summary>Verifies that tagged parameters are correctly skipped. The interface MyTaggedOperationsV0 doesn't
    /// support any of the tagged parameters used by MyTaggedOperations interface.</summary>
    [Test]
    public void Skip_tagged_parameters()
    {
        // Arrange
        var service = new MyTaggedOperationsV0Service();
        var invoker = new ColocInvoker(service);
        var proxy = new MyTaggedOperationsProxy(invoker);

        // Act/Assert
        Assert.That(async () => await proxy.OpAsync(10, 1, 12), Throws.Nothing);
    }

    [Test]
    public async Task Proxy_decoded_from_incoming_response_has_the_invoker_of_the_proxy_that_sent_the_request()
    {
        // Arrange
        var service = new MyOperationsAService();
        var invoker = new ColocInvoker(service);
        var proxy = new MyOperationsAProxy(invoker);

        // Act
        PingableProxy? receivedProxy = await proxy.OpWithProxyReturnValueAsync();

        // Assert
        Assert.That(receivedProxy, Is.Not.Null);
        Assert.That(receivedProxy!.Value.Invoker, Is.EqualTo(((IIceProxy)proxy).Invoker));
    }

    [Test]
    public async Task Proxy_decoded_from_incoming_request_has_invalid_invoker()
    {
        // Arrange
        var service = new MyOperationsAService();
        var invoker = new ColocInvoker(service);
        var proxy = new MyOperationsAProxy(invoker);

        // Act
        await proxy.OpWithProxyParameterAsync(new PingableProxy(InvalidInvoker.Instance, new Uri("icerpc:/hello")));

        // Assert
        Assert.That(service.ReceivedProxy, Is.Not.Null);
        Assert.That(service.ReceivedProxy!.Value.Invoker, Is.EqualTo(InvalidInvoker.Instance));
    }

    [Test]
    public void Call_operation_defined_in_base_interface_defined_in_non_parent_module()
    {
        // Arrange
        var service = new MyDerivedOperationsService();
        var invoker = new ColocInvoker(service);
        var proxy = new MyDerivedOperationsProxy(invoker);

        // Act/Assert
        Assert.That(async () => await proxy.OpAsync(), Throws.Nothing);
    }

    [Service]
    private partial class MyOperationsAService : IMyOperationsAService
    {
        public PingableProxy? ReceivedProxy;

        public ValueTask ContinueAsync(IFeatureCollection features, CancellationToken cancellationToken) => default;

        public ValueTask OpWithoutParametersAndVoidReturnAsync(
            IFeatureCollection features,
            CancellationToken cancellationToken) => default;

        public ValueTask<int> OpWithSingleParameterAndReturnValueAsync(
            int p,
            IFeatureCollection features,
            CancellationToken cancellationToken) => new(p);

        public ValueTask<(int ReturnValue, int R2)> OpWithMultipleParametersAndReturnValuesAsync(
            int p1,
            int p2,
            IFeatureCollection features,
            CancellationToken cancellationToken) => new((p1, p2));

        public ValueTask IdempotentOpAsync(
            IFeatureCollection features,
            CancellationToken cancellationToken) => default;

        public ValueTask OpWithSpecialParameterNamesAsync(
            int cancel,
            int features,
            IFeatureCollection features_,
            CancellationToken cancellationToken) => default;

        public ValueTask<PipeReader> OpWithSingleReturnValueAndMarshaledResultAttributeAsync(
            IFeatureCollection features,
            CancellationToken cancellationToken) =>
            new(IMyOperationsAService.Response.EncodeOpWithSingleReturnValueAndMarshaledResultAttribute(
                new int[] { 1, 2, 3 },
                features.Get<IIceFeature>()?.EncodeOptions));

        public ValueTask<PipeReader> OpWithMultipleReturnValuesAndMarshaledResultAttributeAsync(
            IFeatureCollection features,
            CancellationToken cancellationToken) =>
            new(IMyOperationsAService.Response.EncodeOpWithMultipleReturnValuesAndMarshaledResultAttribute(
                new int[] { 1, 2, 3 },
                new int[] { 1, 2, 3 },
                features.Get<IIceFeature>()?.EncodeOptions));

        public ValueTask<ReadOnlyMemory<int>> OpReadOnlyMemoryAsync(
            int[] p1,
            IFeatureCollection features,
            CancellationToken cancellationToken) => new(p1);

        public ValueTask<ReadOnlyMemory<int>> OpReadOnlyMemoryTaggedAsync(
            int[]? p1,
            IFeatureCollection features,
            CancellationToken cancellationToken) => new(p1);

        public ValueTask OpWithProxyParameterAsync(
            PingableProxy? service,
            IFeatureCollection features,
            CancellationToken cancellationToken)
        {
            ReceivedProxy = service;
            return default;
        }

        public ValueTask<PingableProxy?> OpWithProxyReturnValueAsync(
            IFeatureCollection features,
            CancellationToken cancellationToken) =>
            new(new PingableProxy(InvalidInvoker.Instance, new Uri("icerpc:/hello")));
    }

    [Service]
    private sealed partial class MyDerivedOperationsAService : MyOperationsAService, IMyDerivedOperationsAService
    {
        public ValueTask OpDerivedWithoutParametersAndVoidReturnAsync(
            IFeatureCollection features,
            CancellationToken cancellationToken) => default;

        public ValueTask<int> OpDerivedWithSingleParameterAndReturnValueAsync(
            int p,
            IFeatureCollection features,
            CancellationToken cancellationToken) => new(p);
    }

    [Service]
    private sealed partial class MyTaggedOperationsV0Service : IMyTaggedOperationsV0Service
    {
        public ValueTask OpAsync(int y, IFeatureCollection features, CancellationToken cancellationToken) => default;
    }

    [Service]
    private sealed partial class MyDerivedOperationsService : IMyDerivedOperationsService
    {
        public ValueTask OpAsync(IFeatureCollection features, CancellationToken cancellationToken) => default;
        public ValueTask OpDerivedAsync(IFeatureCollection features, CancellationToken cancellationToken) => default;
    }
}
