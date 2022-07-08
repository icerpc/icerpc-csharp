// Copyright (c) ZeroC, Inc. All rights reserved.

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
        PipeReader responsePayload = ISequenceMappingOperations.Response.OpReturnTuple(
            new int[] { 1, 2, 3 },
            new int[] { 1, 2, 3 });
        var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.IceRpc)
        {
            Payload = responsePayload
        };

        (CustomSequence<int> r1, CustomSequence<int> r2) =
            await SequenceMappingOperationsProxy.Response.OpReturnTupleAsync(
                response,
                request,
                InvalidOperationInvoker.Instance,
                null,
                default);

        Assert.That(r1, Is.EqualTo(new CustomSequence<int>(new int[] { 1, 2, 3 })));
        Assert.That(r2, Is.EqualTo(new CustomSequence<int>(new int[] { 1, 2, 3 })));
    }

    [Test]
    public async Task Return_single_type_using_cs_generic_attribute()
    {
        PipeReader responsePayload = ISequenceMappingOperations.Response.OpReturnSingleType(new int[] { 1, 2, 3 });
        var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.IceRpc)
        {
            Payload = responsePayload
        };

        // TODO bogus mapping this should return CustomSequence<int>
        int[] r =
            await SequenceMappingOperationsProxy.Response.OpReturnSingleTypeAsync(
                response,
                request,
                InvalidOperationInvoker.Instance,
                null,
                default);

        Assert.That(r, Is.EqualTo(new int[] { 1, 2, 3 }));
    }

    [Test]
    public void Parameter_using_cs_generic_attribute()
    {
        // Act
        PipeReader requestPayload = SequenceMappingOperationsProxy.Request.OpSingleParameter(
            new CustomSequence<int>(new int[] { 1, 2, 3 }));

        // Assert
        Assert.That(
            async () => await ISequenceMappingOperations.Request.OpSingleParameterAsync(
                new IncomingRequest(FakeConnectionContext.IceRpc)
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
        var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.IceRpc)
        {
            Payload = SequenceMappingOperationsProxy.Request.OpStructNestedSequence(data)
        };

        ValueTask<IList<IList<MyStruct>>[]> result =
            SequenceMappingOperationsProxy.Response.OpStructNestedSequenceAsync(
                response,
                request,
                InvalidOperationInvoker.Instance,
                null,
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
        var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.IceRpc)
        {
            Payload = SequenceMappingOperationsProxy.Request.OpNumericTypeNestedSequence(data)
        };

        ValueTask<IList<IList<byte>>[]> result =
            SequenceMappingOperationsProxy.Response.OpNumericTypeNestedSequenceAsync(
                response,
                request,
                InvalidOperationInvoker.Instance,
                null,
                default);

        Assert.That(await result, Is.EqualTo(data));
    }
}
