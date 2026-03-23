// Copyright (c) ZeroC, Inc.

using IceRpc.Ice.Codec;
using IceRpc.Tests.Common;
using NUnit.Framework;
using System.IO.Pipelines;
using ZeroC.Tests.Common;

namespace IceRpc.Ice.Generator.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class DictionaryMappingTests
{
    [Test]
    public async Task Operation_returning_a_dictionary_of_int()
    {
        // Arrange
        var value = new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3 };
        PipeReader responsePayload =
            IDictionaryMappingOperationsService.Response.EncodeReturnDictionaryOfInt(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        Dictionary<int, int> decodedValue =
            await DictionaryMappingOperationsProxy.Response.DecodeReturnDictionaryOfIntAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(decodedValue, Is.EqualTo(value));
    }

    [Test]
    public async Task Operation_sending_a_dictionary_of_int()
    {
        // Arrange
        var value = new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3 };

        // Act
        PipeReader requestPayload = DictionaryMappingOperationsProxy.Request.EncodeSendDictionaryOfInt(value);

        // Assert
        using var request = new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
        {
            Payload = requestPayload
        };
        Dictionary<int, int> decodedValue =
            await IDictionaryMappingOperationsService.Request.DecodeSendDictionaryOfIntAsync(
                request,
                default);
        Assert.That(decodedValue, Is.EqualTo(value));
    }

    [Test]
    public async Task Operation_returning_a_dictionary_of_string()
    {
        // Arrange
        var value = new Dictionary<string, string>
        {
            ["0"] = "0",
            ["1"] = "1",
            ["2"] = "2"
        };
        PipeReader responsePayload =
            IDictionaryMappingOperationsService.Response.EncodeReturnDictionaryOfString(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        Dictionary<string, string> decodedValue =
            await DictionaryMappingOperationsProxy.Response.DecodeReturnDictionaryOfStringAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(decodedValue, Is.EqualTo(value));
    }

    [Test]
    public async Task Operation_sending_a_dictionary_of_string()
    {
        // Arrange
        var value = new Dictionary<string, string>
        {
            ["0"] = "0",
            ["1"] = "1",
            ["2"] = "2"
        };

        // Act
        PipeReader requestPayload = DictionaryMappingOperationsProxy.Request.EncodeSendDictionaryOfString(value);

        // Assert
        using var request = new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
        {
            Payload = requestPayload
        };
        Dictionary<string, string> decodedValue =
            await IDictionaryMappingOperationsService.Request.DecodeSendDictionaryOfStringAsync(
                request,
                default);
        Assert.That(decodedValue, Is.EqualTo(value));
    }

    [Test]
    public async Task Operation_returning_a_dictionary_of_enum()
    {
        // Arrange
        var value = new Dictionary<MyEnum, MyEnum>
        {
            [MyEnum.Enum1] = MyEnum.Enum1,
            [MyEnum.Enum2] = MyEnum.Enum2,
            [MyEnum.Enum3] = MyEnum.Enum3
        };
        PipeReader responsePayload =
            IDictionaryMappingOperationsService.Response.EncodeReturnDictionaryOfMyEnum(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        Dictionary<MyEnum, MyEnum> decodedValue =
            await DictionaryMappingOperationsProxy.Response.DecodeReturnDictionaryOfMyEnumAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(decodedValue, Is.EqualTo(value));
    }

    [Test]
    public async Task Operation_sending_a_dictionary_of_enum()
    {
        // Arrange
        var value = new Dictionary<MyEnum, MyEnum>
        {
            [MyEnum.Enum1] = MyEnum.Enum1,
            [MyEnum.Enum2] = MyEnum.Enum2,
            [MyEnum.Enum3] = MyEnum.Enum3
        };

        // Act
        PipeReader requestPayload = DictionaryMappingOperationsProxy.Request.EncodeSendDictionaryOfMyEnum(value);

        // Assert
        using var request = new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
        {
            Payload = requestPayload
        };
        Dictionary<MyEnum, MyEnum> decodedValue =
            await IDictionaryMappingOperationsService.Request.DecodeSendDictionaryOfMyEnumAsync(
                request,
                default);
        Assert.That(decodedValue, Is.EqualTo(value));
    }

    [Test]
    public async Task Operation_returning_a_dictionary_of_struct()
    {
        // Arrange
        var value = new Dictionary<MyStruct, MyStruct>
        {
            [new MyStruct(0, 0)] = new MyStruct(0, 0),
            [new MyStruct(1, 1)] = new MyStruct(1, 1),
            [new MyStruct(2, 2)] = new MyStruct(2, 2)
        };
        PipeReader responsePayload =
            IDictionaryMappingOperationsService.Response.EncodeReturnDictionaryOfMyStruct(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        Dictionary<MyStruct, MyStruct> decodedValue =
            await DictionaryMappingOperationsProxy.Response.DecodeReturnDictionaryOfMyStructAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(decodedValue, Is.EqualTo(value));
    }

    [Test]
    public async Task Operation_sending_a_dictionary_of_struct()
    {
        // Arrange
        var value = new Dictionary<MyStruct, MyStruct>
        {
            [new MyStruct(0, 0)] = new MyStruct(0, 0),
            [new MyStruct(1, 1)] = new MyStruct(1, 1),
            [new MyStruct(2, 2)] = new MyStruct(2, 2)
        };

        // Act
        PipeReader requestPayload =
            DictionaryMappingOperationsProxy.Request.EncodeSendDictionaryOfMyStruct(value);

        // Assert
        using var request = new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
        {
            Payload = requestPayload
        };
        Dictionary<MyStruct, MyStruct> decodedValue =
            await IDictionaryMappingOperationsService.Request.DecodeSendDictionaryOfMyStructAsync(
                request,
                default);
        Assert.That(decodedValue, Is.EqualTo(value));
    }

    [Test]
    public async Task Operation_returning_a_custom_dictionary()
    {
        // Arrange
        var value = new SortedList<int, int> { [1] = 1, [2] = 2, [3] = 3 };
        PipeReader responsePayload =
            IDictionaryMappingOperationsService.Response.EncodeReturnCustomDictionary(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        SortedList<int, int> decodedValue =
            await DictionaryMappingOperationsProxy.Response.DecodeReturnCustomDictionaryAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(decodedValue, Is.EqualTo(value));
    }

    [Test]
    public async Task Operation_sending_a_custom_dictionary()
    {
        // Arrange
        var value = new SortedList<int, int> { [1] = 1, [2] = 2, [3] = 3 };

        // Act
        PipeReader requestPayload = DictionaryMappingOperationsProxy.Request.EncodeSendCustomDictionary(value);

        // Assert
        using var request = new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
        {
            Payload = requestPayload
        };
        SortedList<int, int> decodedValue =
            await IDictionaryMappingOperationsService.Request.DecodeSendCustomDictionaryAsync(
                request,
                default);
        Assert.That(decodedValue, Is.EqualTo(value));
    }

    [Test]
    public async Task Return_and_out_dictionary()
    {
        // Arrange
        var value1 = new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3 };
        var value2 = new Dictionary<int, int> { [4] = 4, [5] = 5, [6] = 6 };
        PipeReader responsePayload =
            IDictionaryMappingOperationsService.Response.EncodeReturnAndOutDictionary(value1, value2);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        (Dictionary<int, int> returnValue, Dictionary<int, int> r1) =
            await DictionaryMappingOperationsProxy.Response.DecodeReturnAndOutDictionaryAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(returnValue, Is.EqualTo(value1));
        Assert.That(r1, Is.EqualTo(value2));
    }

    [Test]
    public async Task Return_and_out_custom_dictionary()
    {
        // Arrange
        var value1 = new SortedList<int, int> { [1] = 1, [2] = 2, [3] = 3 };
        var value2 = new SortedList<int, int> { [4] = 4, [5] = 5, [6] = 6 };
        PipeReader responsePayload =
            IDictionaryMappingOperationsService.Response.EncodeReturnAndOutCustomDictionary(value1, value2);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        (SortedList<int, int> returnValue, SortedList<int, int> r1) =
            await DictionaryMappingOperationsProxy.Response.DecodeReturnAndOutCustomDictionaryAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(returnValue, Is.EqualTo(value1));
        Assert.That(r1, Is.EqualTo(value2));
    }

    [Test]
    public void Decode_struct_with_a_custom_dictionary_field()
    {
        // Arrange
        var value = new StructWithCustomDictionary(
            new SortedList<int, int> { [1] = 1, [2] = 2, [3] = 3 });
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new IceEncoder(buffer, default);
        value.Encode(ref encoder);
        var decoder = new IceDecoder(buffer.WrittenMemory, activator: null);

        // Act
        var decodedValue = new StructWithCustomDictionary(ref decoder);

        // Assert
        Assert.That(decodedValue.Value, Is.EqualTo(value.Value));
    }
}
