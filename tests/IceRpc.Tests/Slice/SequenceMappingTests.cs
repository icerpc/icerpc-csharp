// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Tests;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Slice.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class SequenceMappingTests
{
    [Test]
    public async Task Return_tuple_with_elements_usig_cs_generic_attribute()
    {
        var responsePayload = ISequenceMappingOperations.Response.OpReturnTuple(
            new int[] { 1, 2, 3 },
            new int[] { 1, 2, 3 });
        var request = new OutgoingRequest(new Proxy(Protocol.IceRpc));
        var response = new IncomingResponse(request, InvalidConnection.IceRpc)
        {
            Payload = responsePayload
        };

        (CustomSequence<int> r1, CustomSequence<int> r2) =
            await SequenceMappingOperationsPrx.Response.OpReturnTupleAsync(response, request, default);

        Assert.That(r1, Is.EqualTo(new CustomSequence<int>(new int[] { 1, 2, 3 })));
        Assert.That(r2, Is.EqualTo(new CustomSequence<int>(new int[] { 1, 2, 3 })));
    }

    [Test]
    public async Task Return_single_type_usig_cs_generic_attribute()
    {
        var responsePayload = ISequenceMappingOperations.Response.OpReturnSingleType(new int[] { 1, 2, 3 });
        var request = new OutgoingRequest(new Proxy(Protocol.IceRpc));
        var response = new IncomingResponse(request, InvalidConnection.IceRpc)
        {
            Payload = responsePayload
        };

        // TODO bogus mapping this should return CustomSequence<int>
        int[] r =
            await SequenceMappingOperationsPrx.Response.OpReturnSingleTypeAsync(response, request, default);

        Assert.That(r, Is.EqualTo(new int[] { 1, 2, 3 }));
    }

    [Test]
    public void Parameter_using_cs_generic_attribute()
    {
        // Act
        var requestPayload = SequenceMappingOperationsPrx.Request.OpSingleParameter(
            new CustomSequence<int>(new int[] { 1, 2, 3 }));

        // Assert
        Assert.That(
            async () => await ISequenceMappingOperations.Request.OpSingleParameterAsync(
                new IncomingRequest(InvalidConnection.IceRpc)
                {
                    Payload = requestPayload
                },
                default),
            Throws.Nothing);
    }
}
