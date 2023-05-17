// Copyright (c) ZeroC, Inc.

using IceRpc.Tests.Common;
using NUnit.Framework;
using System.IO.Pipelines;

namespace IceRpc.Tests.Slice;

[Parallelizable(scope: ParallelScope.All)]
public class DictionaryMappingTests
{
    [Test]
    public async Task Operation_returning_a_tuple_with_dictionary_elements()
    {
        PipeReader responsePayload = IDictionaryMappingOperationsService.Response.EncodeReturnCustomDictionaryTuple(
            new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3 },
            new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3 });
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        (CustomDictionary<int, int> r1, CustomDictionary<int, int> r2) =
            await DictionaryMappingOperationsProxy.Response.DecodeReturnCustomDictionaryTupleAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        Assert.That(r1, Is.EqualTo(new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3 }));
        Assert.That(r2, Is.EqualTo(new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3 }));
    }

    [Test]
    public async Task Operation_returning_a_tuple_with_custom_dictionary_elements()
    {
        PipeReader responsePayload = IDictionaryMappingOperationsService.Response.EncodeReturnCustomDictionaryTuple(
            new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3 },
            new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3 });
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        (CustomDictionary<int, int> r1, CustomDictionary<int, int> r2) =
            await DictionaryMappingOperationsProxy.Response.DecodeReturnCustomDictionaryTupleAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        Assert.That(r1, Is.EqualTo(new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3 }));
        Assert.That(r2, Is.EqualTo(new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3 }));
    }

    [Test]
    public async Task Operation_returning_a_dictionary()
    {
        PipeReader responsePayload = IDictionaryMappingOperationsService.Response.EncodeReturnCustomDictionary(
            new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3 });
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        CustomDictionary<int, int> r = await DictionaryMappingOperationsProxy.Response.DecodeReturnCustomDictionaryAsync(
            response,
            request,
            InvalidProxy.Instance,
            default);

        Assert.That(r, Is.EqualTo(new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3 }));
    }

    [Test]
    public async Task Operation_returning_a_custom_dictionary()
    {
        PipeReader responsePayload = IDictionaryMappingOperationsService.Response.EncodeReturnCustomDictionary(
            new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3 });
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        CustomDictionary<int, int> r = await DictionaryMappingOperationsProxy.Response.DecodeReturnCustomDictionaryAsync(
            response,
            request,
            InvalidProxy.Instance,
            default);

        Assert.That(r, Is.EqualTo(new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3 }));
    }

    [Test]
    public void Operation_sending_a_dictionary()
    {
        // Arrange
        PipeReader requestPayload = DictionaryMappingOperationsProxy.Request.EncodeSendCustomDictionary(
            new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3 });

        // Act/Assert
        Assert.That(
            async () => await IDictionaryMappingOperationsService.Request.DecodeSendCustomDictionaryAsync(
                new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
                {
                    Payload = requestPayload
                },
                default),
            Throws.Nothing);
    }

    [Test]
    public void Operation_sending_a_custom_dictionary()
    {
        // Arrange
        PipeReader requestPayload = DictionaryMappingOperationsProxy.Request.EncodeSendCustomDictionary(
            new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3 });

        // Act/Assert
        Assert.That(
            async () => await IDictionaryMappingOperationsService.Request.DecodeSendCustomDictionaryAsync(
                new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
                {
                    Payload = requestPayload
                },
                default),
            Throws.Nothing);
    }
}
