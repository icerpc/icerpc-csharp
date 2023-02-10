// Copyright (c) ZeroC, Inc.

using IceRpc.Tests.Common;
using NUnit.Framework;
using System.IO.Pipelines;

namespace IceRpc.Tests.Slice;

[Parallelizable(scope: ParallelScope.All)]
public class DictionaryMappingTests
{
    [Test]
    public async Task Return_tuple_with_elements_using_cs_generic_attribute()
    {
        PipeReader responsePayload = IDictionaryMappingOperationsService.Response.OpReturnTuple(
            new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3 },
            new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3 });
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        (CustomDictionary<int, int> r1, CustomDictionary<int, int> r2) =
            await DictionaryMappingOperationsProxy.Response.OpReturnTupleAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        Assert.That(r1, Is.EqualTo(new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3 }));
        Assert.That(r2, Is.EqualTo(new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3 }));
    }

    [Test]
    public async Task Return_type_using_cs_generic_attribute()
    {
        PipeReader responsePayload = IDictionaryMappingOperationsService.Response.OpReturnSingleType(
            new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3 });
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        CustomDictionary<int, int> r = await DictionaryMappingOperationsProxy.Response.OpReturnSingleTypeAsync(
            response,
            request,
            InvalidProxy.Instance,
            default);

        Assert.That(r, Is.EqualTo(new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3 }));
    }

    [Test]
    public void Parameter_using_cs_generic_attribute()
    {
        // Arrange
        PipeReader requestPayload = DictionaryMappingOperationsProxy.Request.OpSingleParameter(
            new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3 });

        // Act/Assert
        Assert.That(
            async () => await IDictionaryMappingOperationsService.Request.OpSingleParameterAsync(
                new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
                {
                    Payload = requestPayload
                },
                default),
            Throws.Nothing);
    }
}
