// Copyright (c) ZeroC, Inc.

using IceRpc.Tests.Common;
using NUnit.Framework;
using System.IO.Pipelines;
using ZeroC.Slice;

namespace IceRpc.Slice.Tests;

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
    public async Task Operation_returning_a_dictionary_of_int32()
    {
        // Arrange
        var value = new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3 };
        PipeReader responsePayload =
            IDictionaryMappingOperationsService.Response.EncodeReturnDictionaryOfInt32(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        Dictionary<int, int> r =
            await DictionaryMappingOperationsProxy.Response.DecodeReturnDictionaryOfInt32Async(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(r, Is.EqualTo(value));
    }

    [Test]
    public async Task Operation_returning_a_dictionary_of_string()
    {
        // Arrange
        var value = new Dictionary<string, string> {
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
        Dictionary<string, string> r =
            await DictionaryMappingOperationsProxy.Response.DecodeReturnDictionaryOfStringAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(r, Is.EqualTo(value));
    }

    [Test]
    public async Task Operation_returning_a_dictionary_of_fixed_size_enum()
    {
        // Arrange
        var value = new Dictionary<MyFixedSizeEnum, MyFixedSizeEnum>
        {
            [MyFixedSizeEnum.SEnum1] = MyFixedSizeEnum.SEnum1,
            [MyFixedSizeEnum.SEnum2] = MyFixedSizeEnum.SEnum2,
            [MyFixedSizeEnum.SEnum3] = MyFixedSizeEnum.SEnum3
        };
        PipeReader responsePayload =
            IDictionaryMappingOperationsService.Response.EncodeReturnDictionaryOfMyFixedSizeEnum(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        Dictionary<MyFixedSizeEnum, MyFixedSizeEnum> r =
            await DictionaryMappingOperationsProxy.Response.DecodeReturnDictionaryOfMyFixedSizeEnumAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(r, Is.EqualTo(value));
    }

    [Test]
    public async Task Operation_returning_a_dictionary_of_var_size_enum()
    {
        // Arrange
        var value = new Dictionary<MyVarSizeEnum, MyVarSizeEnum>
        {
            [MyVarSizeEnum.Enum1] = MyVarSizeEnum.Enum1,
            [MyVarSizeEnum.Enum2] = MyVarSizeEnum.Enum2,
            [MyVarSizeEnum.Enum3] = MyVarSizeEnum.Enum3
        };
        PipeReader responsePayload =
            IDictionaryMappingOperationsService.Response.EncodeReturnDictionaryOfMyVarSizeEnum(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        Dictionary<MyVarSizeEnum, MyVarSizeEnum> r =
            await DictionaryMappingOperationsProxy.Response.DecodeReturnDictionaryOfMyVarSizeEnumAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(r, Is.EqualTo(value));
    }

    [Test]
    public async Task Operation_returning_a_dictionary_of_unchecked_enum()
    {
        // Arrange
        var value = new Dictionary<MyUncheckedEnum, MyUncheckedEnum>
        {
            [MyUncheckedEnum.E1] = MyUncheckedEnum.E1,
            [MyUncheckedEnum.E2] = MyUncheckedEnum.E2,
            [MyUncheckedEnum.E3] = (MyUncheckedEnum)20
        };
        PipeReader responsePayload =
            IDictionaryMappingOperationsService.Response.EncodeReturnDictionaryOfMyUncheckedEnum(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        Dictionary<MyUncheckedEnum, MyUncheckedEnum> r =
            await DictionaryMappingOperationsProxy.Response.DecodeReturnDictionaryOfMyUncheckedEnumAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(r, Is.EqualTo(value));
    }

    [Test]
    public async Task Operation_returning_a_dictionary_of_compact_struct()
    {
        // Arrange
        var value = new Dictionary<MyCompactStruct, MyCompactStruct>
        {
            [new MyCompactStruct(0, 0)] = new MyCompactStruct(0, 0),
            [new MyCompactStruct(1, 1)] = new MyCompactStruct(1, 1),
            [new MyCompactStruct(2, 2)] = new MyCompactStruct(2, 2)
        };
        PipeReader responsePayload =
            IDictionaryMappingOperationsService.Response.EncodeReturnDictionaryOfMyCompactStruct(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        Dictionary<MyCompactStruct, MyCompactStruct> r =
            await DictionaryMappingOperationsProxy.Response.DecodeReturnDictionaryOfMyCompactStructAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(r, Is.EqualTo(value));
    }

    [Test]
    public async Task Operation_returning_a_dictionary_of_struct()
    {
        // Arrange
        var value = new Dictionary<string, MyStruct>
        {
            ["0"] = new MyStruct(0, 0),
            ["1"] = new MyStruct(1, 1),
            ["2"] = new MyStruct(2, 2)
        };
        PipeReader responsePayload =
            IDictionaryMappingOperationsService.Response.EncodeReturnDictionaryOfMyStruct(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        Dictionary<string, MyStruct> r =
            await DictionaryMappingOperationsProxy.Response.DecodeReturnDictionaryOfMyStructAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(r, Is.EqualTo(value));
    }

    [Test]
    public async Task Operation_returning_a_dictionary_of_optional_int32()
    {
        // Arrange
        var value = new Dictionary<int, int?> { [1] = 1, [2] = null, [3] = 3 };
        PipeReader responsePayload =
            IDictionaryMappingOperationsService.Response.EncodeReturnDictionaryOfOptionalInt32(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        Dictionary<int, int?> r =
            await DictionaryMappingOperationsProxy.Response.DecodeReturnDictionaryOfOptionalInt32Async(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(r, Is.EqualTo(value));
    }

    [Test]
    public async Task Operation_returning_a_dictionary_of_optional_string()
    {
        // Arrange
        var value = new Dictionary<string, string?>
        {
            ["0"] = "0",
            ["1"] = null,
            ["2"] = "2"
        };
        PipeReader responsePayload =
            IDictionaryMappingOperationsService.Response.EncodeReturnDictionaryOfOptionalString(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        Dictionary<string, string?> r =
            await DictionaryMappingOperationsProxy.Response.DecodeReturnDictionaryOfOptionalStringAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(r, Is.EqualTo(value));
    }

    [Test]
    public async Task Operation_returning_a_dictionary_of_optional_fixed_size_enum()
    {
        // Arrange
        var value = new Dictionary<MyFixedSizeEnum, MyFixedSizeEnum?>
        {
            [MyFixedSizeEnum.SEnum1] = MyFixedSizeEnum.SEnum1,
            [MyFixedSizeEnum.SEnum2] = null,
            [MyFixedSizeEnum.SEnum3] = MyFixedSizeEnum.SEnum3
        };
        PipeReader responsePayload =
            IDictionaryMappingOperationsService.Response.EncodeReturnDictionaryOfOptionalMyFixedSizeEnum(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        Dictionary<MyFixedSizeEnum, MyFixedSizeEnum?> r =
            await DictionaryMappingOperationsProxy.Response.DecodeReturnDictionaryOfOptionalMyFixedSizeEnumAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(r, Is.EqualTo(value));
    }

    [Test]
    public async Task Operation_returning_a_dictionary_of_optional_var_size_enum()
    {
        // Arrange
        var value = new Dictionary<MyVarSizeEnum, MyVarSizeEnum?>
        {
            [MyVarSizeEnum.Enum1] = MyVarSizeEnum.Enum1,
            [MyVarSizeEnum.Enum2] = null,
            [MyVarSizeEnum.Enum3] = MyVarSizeEnum.Enum3
        };
        PipeReader responsePayload =
            IDictionaryMappingOperationsService.Response.EncodeReturnDictionaryOfOptionalMyVarSizeEnum(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        Dictionary<MyVarSizeEnum, MyVarSizeEnum?> r =
            await DictionaryMappingOperationsProxy.Response.DecodeReturnDictionaryOfOptionalMyVarSizeEnumAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(r, Is.EqualTo(value));
    }

    [Test]
    public async Task Operation_returning_a_dictionary_of_optional_unchecked_enum()
    {
        // Arrange
        var value = new Dictionary<MyUncheckedEnum, MyUncheckedEnum?>
        {
            [MyUncheckedEnum.E1] = MyUncheckedEnum.E1,
            [MyUncheckedEnum.E2] = null,
            [MyUncheckedEnum.E3] = (MyUncheckedEnum)20
        };
        PipeReader responsePayload =
            IDictionaryMappingOperationsService.Response.EncodeReturnDictionaryOfOptionalMyUncheckedEnum(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        Dictionary<MyUncheckedEnum, MyUncheckedEnum?> r =
            await DictionaryMappingOperationsProxy.Response.DecodeReturnDictionaryOfOptionalMyUncheckedEnumAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(r, Is.EqualTo(value));
    }

    [Test]
    public async Task Operation_returning_a_dictionary_of_optional_compact_struct()
    {
        // Arrange
        var value = new Dictionary<MyCompactStruct, MyCompactStruct?>
        {
            [new MyCompactStruct(0, 0)] = new MyCompactStruct(0, 0),
            [new MyCompactStruct(1, 1)] = null,
            [new MyCompactStruct(2, 2)] = new MyCompactStruct(2, 2)
        };
        PipeReader responsePayload =
            IDictionaryMappingOperationsService.Response.EncodeReturnDictionaryOfOptionalMyCompactStruct(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        Dictionary<MyCompactStruct, MyCompactStruct?> r =
            await DictionaryMappingOperationsProxy.Response.DecodeReturnDictionaryOfOptionalMyCompactStructAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(r, Is.EqualTo(value));
    }

    [Test]
    public async Task Operation_returning_a_dictionary_of_optional_struct()
    {
        // Arrange
        var value = new Dictionary<string, MyStruct?>
        {
            ["0"] = new MyStruct(0, 0),
            ["1"] = null,
            ["2"] = new MyStruct(2, 2)
        };
        PipeReader responsePayload =
            IDictionaryMappingOperationsService.Response.EncodeReturnDictionaryOfOptionalMyStruct(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        Dictionary<string, MyStruct?> r =
            await DictionaryMappingOperationsProxy.Response.DecodeReturnDictionaryOfOptionalMyStructAsync(
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
        CustomDictionary<int, int> r =
            await DictionaryMappingOperationsProxy.Response.DecodeReturnCustomDictionaryAsync(
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
        var value = new List<KeyValuePair<int, int>>()
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
        List<KeyValuePair<int, int>> r =
            await DictionaryMappingOperationsProxy.Response.DecodeReturnCustomDictionary2Async(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(r, Is.EqualTo(value));
    }

    [Test]
    public async Task Operation_sending_a_dictionary_of_int32()
    {
        // Arrange
        var value = new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3 };

        // Act
        var requestPayload = DictionaryMappingOperationsProxy.Request.EncodeSendDictionaryOfInt32(value);

        // Assert
        using var request = new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
        {
            Payload = requestPayload
        };
        Dictionary<int, int> decodedValue =
            await IDictionaryMappingOperationsService.Request.DecodeSendDictionaryOfInt32Async(
                request,
                default);
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
        var requestPayload = DictionaryMappingOperationsProxy.Request.EncodeSendDictionaryOfString(value);

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
    public async Task Operation_sending_a_dictionary_of_fixed_size_enum()
    {
        // Arrange
        var value = new Dictionary<MyFixedSizeEnum, MyFixedSizeEnum>
        {
            [MyFixedSizeEnum.SEnum1] = MyFixedSizeEnum.SEnum1,
            [MyFixedSizeEnum.SEnum2] = MyFixedSizeEnum.SEnum2,
            [MyFixedSizeEnum.SEnum3] = MyFixedSizeEnum.SEnum3
        };

        // Act
        var requestPayload = DictionaryMappingOperationsProxy.Request.EncodeSendDictionaryOfMyFixedSizeEnum(value);

        // Assert
        using var request = new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
        {
            Payload = requestPayload
        };
        Dictionary<MyFixedSizeEnum, MyFixedSizeEnum> decodedValue =
            await IDictionaryMappingOperationsService.Request.DecodeSendDictionaryOfMyFixedSizeEnumAsync(
                request,
                default);
        Assert.That(decodedValue, Is.EqualTo(value));
    }

    [Test]
    public async Task Operation_sending_a_dictionary_of_var_size_enum()
    {
        // Arrange
        var value = new Dictionary<MyVarSizeEnum, MyVarSizeEnum>
        {
            [MyVarSizeEnum.Enum1] = MyVarSizeEnum.Enum1,
            [MyVarSizeEnum.Enum2] = MyVarSizeEnum.Enum2,
            [MyVarSizeEnum.Enum3] = MyVarSizeEnum.Enum3
        };

        // Act
        var requestPayload = DictionaryMappingOperationsProxy.Request.EncodeSendDictionaryOfMyVarSizeEnum(value);

        // Assert
        using var request = new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
        {
            Payload = requestPayload
        };
        Dictionary<MyVarSizeEnum, MyVarSizeEnum> decodedValue =
            await IDictionaryMappingOperationsService.Request.DecodeSendDictionaryOfMyVarSizeEnumAsync(
                request,
                default);
        Assert.That(decodedValue, Is.EqualTo(value));
    }

    [Test]
    public async Task Operation_sending_a_dictionary_of_unchecked_enum()
    {
        // Arrange
        var value = new Dictionary<MyUncheckedEnum, MyUncheckedEnum>
        {
            [MyUncheckedEnum.E1] = MyUncheckedEnum.E1,
            [MyUncheckedEnum.E2] = MyUncheckedEnum.E2,
            [MyUncheckedEnum.E3] = (MyUncheckedEnum)20
        };

        // Act
        var requestPayload = DictionaryMappingOperationsProxy.Request.EncodeSendDictionaryOfMyUncheckedEnum(value);

        // Assert
        using var request = new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
        {
            Payload = requestPayload
        };
        Dictionary<MyUncheckedEnum, MyUncheckedEnum> decodedValue =
            await IDictionaryMappingOperationsService.Request.DecodeSendDictionaryOfMyUncheckedEnumAsync(
                request,
                default);
        Assert.That(decodedValue, Is.EqualTo(value));
    }

    [Test]
    public async Task Operation_sending_a_dictionary_of_compact_structs()
    {
        // Arrange
        var value = new Dictionary<MyCompactStruct, MyCompactStruct>
        {
            [new MyCompactStruct(0, 0)] = new MyCompactStruct(0, 0),
            [new MyCompactStruct(1, 1)] = new MyCompactStruct(1, 1),
            [new MyCompactStruct(2, 2)] = new MyCompactStruct(2, 2)
        };

        // Act
        var requestPayload = DictionaryMappingOperationsProxy.Request.EncodeSendDictionaryOfMyCompactStruct(value);

        // Assert
        using var request = new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
        {
            Payload = requestPayload
        };
        Dictionary<MyCompactStruct, MyCompactStruct> decodedValue =
            await IDictionaryMappingOperationsService.Request.DecodeSendDictionaryOfMyCompactStructAsync(
                request,
                default);
        Assert.That(decodedValue, Is.EqualTo(value));
    }

    [Test]
    public async Task Operation_sending_a_dictionary_of_structs()
    {
        // Arrange
        var value = new Dictionary<string, MyStruct>
        {
            ["0"] = new MyStruct(0, 0),
            ["1"] = new MyStruct(1, 1),
            ["2"] = new MyStruct(2, 2)
        };

        // Act
        var requestPayload = DictionaryMappingOperationsProxy.Request.EncodeSendDictionaryOfMyStruct(value);

        // Assert
        using var request = new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
        {
            Payload = requestPayload
        };
        Dictionary<string, MyStruct> decodedValue =
            await IDictionaryMappingOperationsService.Request.DecodeSendDictionaryOfMyStructAsync(
                request,
                default);
        Assert.That(decodedValue, Is.EqualTo(value));
    }

    [Test]
    public async Task Operation_sending_a_dictionary_of_optional_int32()
    {
        // Arrange
        var value = new Dictionary<int, int?> { [1] = 1, [2] = null, [3] = 3 };

        // Act
        var requestPayload = DictionaryMappingOperationsProxy.Request.EncodeSendDictionaryOfOptionalInt32(value);

        // Assert
        using var request = new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
        {
            Payload = requestPayload
        };
        Dictionary<int, int?> decodedValue =
            await IDictionaryMappingOperationsService.Request.DecodeSendDictionaryOfOptionalInt32Async(
                request,
                default);
        Assert.That(decodedValue, Is.EqualTo(value));
    }

    [Test]
    public async Task Operation_sending_a_dictionary_of_optional_string()
    {
        // Arrange
        var value = new Dictionary<string, string?>
        {
            ["0"] = "0",
            ["1"] = null,
            ["2"] = "2"
        };

        // Act
        var requestPayload = DictionaryMappingOperationsProxy.Request.EncodeSendDictionaryOfOptionalString(value);

        // Assert
        using var request = new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
        {
            Payload = requestPayload
        };
        Dictionary<string, string?> decodedValue =
            await IDictionaryMappingOperationsService.Request.DecodeSendDictionaryOfOptionalStringAsync(
                request,
                default);
        Assert.That(decodedValue, Is.EqualTo(value));
    }

    [Test]
    public async Task Operation_sending_a_dictionary_of_optional_fixed_size_enum()
    {
        // Arrange
        var value = new Dictionary<MyFixedSizeEnum, MyFixedSizeEnum?>
        {
            [MyFixedSizeEnum.SEnum1] = MyFixedSizeEnum.SEnum1,
            [MyFixedSizeEnum.SEnum2] = null,
            [MyFixedSizeEnum.SEnum3] = MyFixedSizeEnum.SEnum3
        };

        // Act
        var requestPayload =
            DictionaryMappingOperationsProxy.Request.EncodeSendDictionaryOfOptionalMyFixedSizeEnum(value);

        // Assert
        using var request = new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
        {
            Payload = requestPayload
        };
        Dictionary<MyFixedSizeEnum, MyFixedSizeEnum?> decodedValue =
            await IDictionaryMappingOperationsService.Request.DecodeSendDictionaryOfOptionalMyFixedSizeEnumAsync(
                request,
                default);
        Assert.That(decodedValue, Is.EqualTo(value));
    }

    [Test]
    public async Task Operation_sending_a_dictionary_of_optional_var_size_enum()
    {
        // Arrange
        var value = new Dictionary<MyVarSizeEnum, MyVarSizeEnum?>
        {
            [MyVarSizeEnum.Enum1] = MyVarSizeEnum.Enum1,
            [MyVarSizeEnum.Enum2] = null,
            [MyVarSizeEnum.Enum3] = MyVarSizeEnum.Enum3
        };

        // Act
        var requestPayload =
            DictionaryMappingOperationsProxy.Request.EncodeSendDictionaryOfOptionalMyVarSizeEnum(value);

        // Assert
        using var request = new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
        {
            Payload = requestPayload
        };
        Dictionary<MyVarSizeEnum, MyVarSizeEnum?> decodedValue =
            await IDictionaryMappingOperationsService.Request.DecodeSendDictionaryOfOptionalMyVarSizeEnumAsync(
                request,
                default);
        Assert.That(decodedValue, Is.EqualTo(value));
    }

    [Test]
    public async Task Operation_sending_a_dictionary_of_optional_unchecked_enum()
    {
        // Arrange
        var value = new Dictionary<MyUncheckedEnum, MyUncheckedEnum?>
        {
            [MyUncheckedEnum.E1] = MyUncheckedEnum.E1,
            [MyUncheckedEnum.E2] = null,
            [MyUncheckedEnum.E3] = (MyUncheckedEnum)20
        };

        // Act
        var requestPayload =
            DictionaryMappingOperationsProxy.Request.EncodeSendDictionaryOfOptionalMyUncheckedEnum(value);

        // Assert
        using var request = new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
        {
            Payload = requestPayload
        };
        Dictionary<MyUncheckedEnum, MyUncheckedEnum?> decodedValue =
            await IDictionaryMappingOperationsService.Request.DecodeSendDictionaryOfOptionalMyUncheckedEnumAsync(
                request,
                default);
        Assert.That(decodedValue, Is.EqualTo(value));
    }

    [Test]
    public async Task Operation_sending_a_dictionary_of_optional_compact_structs()
    {
        // Arrange
        var value = new Dictionary<MyCompactStruct, MyCompactStruct?>
        {
            [new MyCompactStruct(0, 0)] = new MyCompactStruct(0, 0),
            [new MyCompactStruct(1, 1)] = null,
            [new MyCompactStruct(2, 2)] = new MyCompactStruct(2, 2)
        };

        // Act
        var requestPayload =
            DictionaryMappingOperationsProxy.Request.EncodeSendDictionaryOfOptionalMyCompactStruct(value);

        // Assert
        using var request = new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
        {
            Payload = requestPayload
        };
        Dictionary<MyCompactStruct, MyCompactStruct?> decodedValue =
            await IDictionaryMappingOperationsService.Request.DecodeSendDictionaryOfOptionalMyCompactStructAsync(
                request,
                default);
        Assert.That(decodedValue, Is.EqualTo(value));
    }

    [Test]
    public async Task Operation_sending_a_dictionary_of_optional_structs()
    {
        // Arrange
        var value = new Dictionary<string, MyStruct?>
        {
            ["0"] = new MyStruct(0, 0),
            ["1"] = null,
            ["2"] = new MyStruct(2, 2)
        };

        // Act
        var requestPayload = DictionaryMappingOperationsProxy.Request.EncodeSendDictionaryOfOptionalMyStruct(value);

        // Assert
        using var request = new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
        {
            Payload = requestPayload
        };
        Dictionary<string, MyStruct?> decodedValue =
            await IDictionaryMappingOperationsService.Request.DecodeSendDictionaryOfOptionalMyStructAsync(
                request,
                default);
        Assert.That(decodedValue, Is.EqualTo(value));
    }

    [Test]
    public async Task Operation_sending_a_custom_dictionary()
    {
        // Arrange
        var value = new CustomDictionary<int, int> { [1] = 1, [2] = 2, [3] = 3 };

        // Act
        var requestPayload = DictionaryMappingOperationsProxy.Request.EncodeSendCustomDictionary(value);

        // Assert
        using var request = new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
        {
            Payload = requestPayload
        };
        var decodedValue = await IDictionaryMappingOperationsService.Request.DecodeSendCustomDictionaryAsync(
            request,
            default);
        Assert.That(decodedValue, Is.EqualTo(value));
    }

    [Test]
    public async Task Operation_sending_a_custom_dictionary2()
    {
        // Arrange
        var value = new List<KeyValuePair<int, int>>
        {
            new KeyValuePair<int, int>(1, 1),
            new KeyValuePair<int, int>(2, 2),
            new KeyValuePair<int, int>(3, 3)
        };

        // Act
        var requestPayload = DictionaryMappingOperationsProxy.Request.EncodeSendCustomDictionary2(value);

        // Assert
        using var request = new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
        {
            Payload = requestPayload
        };
        var decodedValue = await IDictionaryMappingOperationsService.Request.DecodeSendCustomDictionary2Async(
            request,
            default);
        Assert.That(decodedValue, Is.EqualTo(value));
    }

    [Test]
    public void Decode_struct_with_a_custom_dictionary_field()
    {
        // Arrange
        var value = new StructWithCustomDictionary
        {
            Value = new CustomDictionary<int, int> { [1] = 1, [2] = 2, [3] = 3 }
        };
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        value.Encode(ref encoder);
        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);

        // Act
        var decodedValue = new StructWithCustomDictionary(ref decoder);

        // Assert
        Assert.That(decodedValue.Value, Is.EqualTo(value.Value));
    }
}
