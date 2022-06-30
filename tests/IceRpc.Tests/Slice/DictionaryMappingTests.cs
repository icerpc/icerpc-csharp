// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Tests.Common;
using NUnit.Framework;
using System.IO.Pipelines;

namespace IceRpc.Tests.Slice;

[Parallelizable(scope: ParallelScope.All)]
public class DictionaryMappingTests
{
    [Test]
    public async Task Return_tuple_with_elements_usig_cs_generic_attribute()
    {
        PipeReader responsePayload = IDictionaryMappingOperations.Response.OpReturnTuple(
            new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3 },
            new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3 });
        var request = new OutgoingRequest(Proxy.Parse("icerpc:"));
        var response = new IncomingResponse(request, InvalidConnection.IceRpc)
        {
            Payload = responsePayload
        };

        (CustomDictionary<int, int> r1, CustomDictionary<int, int> r2) =
            await DictionaryMappingOperationsPrx.Response.OpReturnTupleAsync(response, request, null, default);

        Assert.That(r1, Is.EqualTo(new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3 }));
        Assert.That(r2, Is.EqualTo(new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3 }));
    }

    [Test]
    public async Task Return_type_using_cs_generic_attribute()
    {
        PipeReader responsePayload = IDictionaryMappingOperations.Response.OpReturnSingleType(
            new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3 });
        var request = new OutgoingRequest(Proxy.Parse("icerpc:"));
        var response = new IncomingResponse(request, InvalidConnection.IceRpc)
        {
            Payload = responsePayload
        };

        // TODO bogus mapping, this should return CustomDictionary<int, int>
        Dictionary<int, int> r = await DictionaryMappingOperationsPrx.Response.OpReturnSingleTypeAsync(
            response,
            request,
            encodeFeature: null,
            default);

        Assert.That(r, Is.EqualTo(new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3 }));
    }

    [Test]
    public void Parameter_using_cs_generic_attribute()
    {
        // Arrange
        PipeReader requestPayload = DictionaryMappingOperationsPrx.Request.OpSingleParameter(
            new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3 });

        // Act/Assert
        Assert.That(
            async () => await IDictionaryMappingOperations.Request.OpSingleParameterAsync(
                new IncomingRequest(InvalidConnection.IceRpc)
                {
                    Payload = requestPayload
                },
                default),
            Throws.Nothing);
    }
}
