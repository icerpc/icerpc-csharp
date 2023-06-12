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
        // Arrange
        var value1 = new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3 };
        var value2 = new Dictionary<int, int> { [4] = 4, [5] = 5, [6] = 6 };
        PipeReader responsePayload = IDictionaryMappingOperationsService.Response.EncodeReturnCustomDictionaryTuple(
            value1,
            value2);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        (CustomDictionary<int, int> r1, CustomDictionary<int, int> r2) =
            await DictionaryMappingOperationsProxy.Response.DecodeReturnCustomDictionaryTupleAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(r1, Is.EqualTo(value1));
        Assert.That(r2, Is.EqualTo(value2));
    }

    [Test]
    public async Task Operation_returning_a_tuple_with_custom_dictionary_elements()
    {
        // Arrange
        var value1 = new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3 };
        var value2 = new Dictionary<int, int> { [4] = 4, [5] = 5, [6] = 6 };
        PipeReader responsePayload = IDictionaryMappingOperationsService.Response.EncodeReturnCustomDictionaryTuple(
            value1,
            value2);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        (CustomDictionary<int, int> r1, CustomDictionary<int, int> r2) =
            await DictionaryMappingOperationsProxy.Response.DecodeReturnCustomDictionaryTupleAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(r1, Is.EqualTo(value1));
        Assert.That(r2, Is.EqualTo(value2));
    }

    [Test]
    public async Task Operation_returning_a_dictionary()
    {
        // Arrange
        var value = new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3 };
        PipeReader responsePayload = IDictionaryMappingOperationsService.Response.EncodeReturnCustomDictionary(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        CustomDictionary<int, int> r = await DictionaryMappingOperationsProxy.Response.DecodeReturnCustomDictionaryAsync(
            response,
            request,
            InvalidProxy.Instance,
            default);

        // Assert
        Assert.That(r, Is.EqualTo(value));
    }

    [Test]
    public async Task Operation_returning_a_custom_dictionary()
    {
        // Arrange
        var value = new CustomDictionary<int, int> { [1] = 1, [2] = 2, [3] = 3 };
        PipeReader responsePayload = IDictionaryMappingOperationsService.Response.EncodeReturnCustomDictionary(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        CustomDictionary<int, int> r = await DictionaryMappingOperationsProxy.Response.DecodeReturnCustomDictionaryAsync(
            response,
            request,
            InvalidProxy.Instance,
            default);

        // Assert
        Assert.That(r, Is.EqualTo(value));
    }

    [Test]
    public async Task Operation_returning_a_custom_dictionary2()
    {
        // Arrange
        var value = new CustomDictionary2<int, int>
        {
            new KeyValuePair<int, int>(1, 1),
            new KeyValuePair<int, int>(2, 2),
            new KeyValuePair<int, int>(3, 3)
        };
        PipeReader responsePayload = IDictionaryMappingOperationsService.Response.EncodeReturnCustomDictionary2(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        CustomDictionary2<int, int> r = await DictionaryMappingOperationsProxy.Response.DecodeReturnCustomDictionary2Async(
            response,
            request,
            InvalidProxy.Instance,
            default);

        // Assert
        Assert.That(r, Is.EqualTo(value));
    }

    [Test]
    public async Task Operation_returning_a_struct_with_a_custom_dictionary()
    {
        // Arrange
        var value = new StructWithCustomDictionary
        {
            Value = new CustomDictionary<int, int> { [1] = 1, [2] = 2, [3] = 3 }
        };
        PipeReader responsePayload =
            IDictionaryMappingOperationsService.Response.EncodeReturnStructWithCustomDictionary(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        StructWithCustomDictionary r =
            await DictionaryMappingOperationsProxy.Response.DecodeReturnStructWithCustomDictionaryAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(r.Value, Is.EqualTo(value.Value));
    }

    [Test]
    public async Task Operation_sending_a_dictionary()
    {
        // Arrange
        var value = new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3 };
        using var request = new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
        {
            Payload = DictionaryMappingOperationsProxy.Request.EncodeSendCustomDictionary(value)
        };

        // Act
        var decodedValue = await IDictionaryMappingOperationsService.Request.DecodeSendCustomDictionaryAsync(
            request,
            default);

        // Assert
        Assert.That(decodedValue, Is.EqualTo(value));
    }

    [Test]
    public async Task Operation_sending_a_custom_dictionary()
    {
        // Arrange
        var value = new CustomDictionary<int, int> { [1] = 1, [2] = 2, [3] = 3 };
        using var request = new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
        {
            Payload = DictionaryMappingOperationsProxy.Request.EncodeSendCustomDictionary(value)
        };

        // Act
        var decodedValue = await IDictionaryMappingOperationsService.Request.DecodeSendCustomDictionaryAsync(
            request,
            default);

        // Assert
        Assert.That(decodedValue, Is.EqualTo(value));
    }

    [Test]
    public async Task Operation_sending_a_custom_dictionary2()
    {
        // Arrange
        var value = new CustomDictionary2<int, int>
        {
            new KeyValuePair<int, int>(1, 1),
            new KeyValuePair<int, int>(2, 2),
            new KeyValuePair<int, int>(3, 3)
        };

        using var request = new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
        {
            Payload = DictionaryMappingOperationsProxy.Request.EncodeSendCustomDictionary2(value)
        };

        // Act
        var decodedValue = await IDictionaryMappingOperationsService.Request.DecodeSendCustomDictionary2Async(
            request,
            default);

        // Assert
        Assert.That(decodedValue, Is.EqualTo(value));
    }

    [Test]
    public async Task Operation_sending_a_struct_with_a_custom_dictionary2()
    {
        // Arrange
        var value = new StructWithCustomDictionary
        {
            Value = new CustomDictionary<int, int> { [1] = 1, [2] = 2, [3] = 3 }
        };

        using var request = new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
        {
            Payload = DictionaryMappingOperationsProxy.Request.EncodeSendStructWithCustomDictionary(value)
        };

        // Act
        var decodedValue = await IDictionaryMappingOperationsService.Request.DecodeSendStructWithCustomDictionaryAsync(
            request,
            default);

        // Assert
        Assert.That(decodedValue.Value, Is.EqualTo(value.Value));
    }
}
