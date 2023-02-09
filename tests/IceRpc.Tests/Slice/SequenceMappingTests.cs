// Copyright (c) ZeroC, Inc.

using IceRpc.Slice;
using IceRpc.Tests.Common;
using NUnit.Framework;
using System.IO.Pipelines;

namespace IceRpc.Tests.Slice;

[Parallelizable(scope: ParallelScope.All)]
public class SequenceMappingTests
{
    [Test]
    public async Task Return_tuple_with_elements_using_cs_generic_attribute()
    {
        PipeReader responsePayload = ISequenceMappingOperationsService.Response.OpReturnTuple(
            new int[] { 1, 2, 3 },
            new int[] { 1, 2, 3 });
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        (CustomSequence<int> r1, CustomSequence<int> r2) =
            await SequenceMappingOperationsProxy.Response.OpReturnTupleAsync(
                response,
                request,
                new PingableProxy(NotImplementedInvoker.Instance),
                default);

        Assert.That(r1, Is.EqualTo(new CustomSequence<int>(new int[] { 1, 2, 3 })));
        Assert.That(r2, Is.EqualTo(new CustomSequence<int>(new int[] { 1, 2, 3 })));
    }

    [Test]
    public async Task Return_single_type_using_cs_generic_attribute()
    {
        PipeReader responsePayload = ISequenceMappingOperationsService.Response.OpReturnSingleType(
            new int[] { 1, 2, 3 });
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        CustomSequence<int> r =
            await SequenceMappingOperationsProxy.Response.OpReturnSingleTypeAsync(
                response,
                request,
                new PingableProxy(NotImplementedInvoker.Instance),
                default);

        Assert.That(r, Is.EqualTo(new CustomSequence<int>(new int[] { 1, 2, 3 })));
    }

    [Test]
    public void Parameter_using_cs_generic_attribute()
    {
        // Act
        PipeReader requestPayload = SequenceMappingOperationsProxy.Request.OpSingleParameter(
            new CustomSequence<int>(new int[] { 1, 2, 3 }));

        // Assert
        Assert.That(
            async () => await ISequenceMappingOperationsService.Request.OpSingleParameterAsync(
                new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
                {
                    Payload = requestPayload
                },
                default),
            Throws.Nothing);
    }

    [Test]
    public async Task Struct_nested_sequence_parameter()
    {
        var data = new IList<IList<MyStruct>>[]
        {
            new List<IList<MyStruct>>()
            {
                new List<MyStruct>()
                {
                    new MyStruct(1, 2),
                    new MyStruct(2, 4),
                    new MyStruct(4, 8)
                },
            },
        };
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = SequenceMappingOperationsProxy.Request.OpStructNestedSequence(data)
        };

        ValueTask<IList<IList<MyStruct>>[]> result =
            SequenceMappingOperationsProxy.Response.OpStructNestedSequenceAsync(
                response,
                request,
                new PingableProxy(NotImplementedInvoker.Instance),
                default);

        Assert.That(await result, Is.EqualTo(data));
    }

    [Test]
    public async Task Numeric_nested_sequence_parameter()
    {
        var data = new IList<IList<byte>>[]
        {
            new List<IList<byte>>()
            {
                new List<byte>() { 1, 2, 3 },
            },
        };
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = SequenceMappingOperationsProxy.Request.OpNumericTypeNestedSequence(data)
        };

        ValueTask<IList<IList<byte>>[]> result =
            SequenceMappingOperationsProxy.Response.OpNumericTypeNestedSequenceAsync(
                response,
                request,
                new PingableProxy(NotImplementedInvoker.Instance),
                default);

        Assert.That(await result, Is.EqualTo(data));
    }
}
