// Copyright (c) ZeroC, Inc.

using IceRpc.Tests.Common;
using NUnit.Framework;
using System.IO.Pipelines;

namespace IceRpc.Ice.Generator.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class SequenceMappingTests
{
    [Test]
    public async Task Operation_returning_a_sequence_of_int()
    {
        // Arrange
        var value = new int[] { 1, 2, 3 };
        PipeReader responsePayload = ISequenceMappingOperationsService.Response.EncodeReturnSequenceOfInt(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        int[] decodedValue =
            await SequenceMappingOperationsProxy.Response.DecodeReturnSequenceOfIntAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(decodedValue, Is.EqualTo(value));
    }

    [Test]
    public async Task Operation_sending_a_sequence_of_int()
    {
        // Arrange
        var value = new int[] { 1, 2, 3 };

        // Act
        PipeReader requestPayload = SequenceMappingOperationsProxy.Request.EncodeSendSequenceOfInt(value);

        // Assert
        using var request = new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
        {
            Payload = requestPayload
        };
        var decodedValue = await ISequenceMappingOperationsService.Request.DecodeSendSequenceOfIntAsync(
            request,
            default);
        Assert.That(decodedValue, Is.EqualTo(value));
    }

    [Test]
    public async Task Operation_returning_a_sequence_of_string()
    {
        // Arrange
        var value = new string[] { "one", "two", "three" };
        PipeReader responsePayload = ISequenceMappingOperationsService.Response.EncodeReturnSequenceOfString(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        string[] decodedValue =
            await SequenceMappingOperationsProxy.Response.DecodeReturnSequenceOfStringAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(decodedValue, Is.EqualTo(value));
    }

    [Test]
    public async Task Operation_sending_a_sequence_of_string()
    {
        // Arrange
        var value = new string[] { "one", "two", "three" };

        // Act
        PipeReader requestPayload = SequenceMappingOperationsProxy.Request.EncodeSendSequenceOfString(value);

        // Assert
        using var request = new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
        {
            Payload = requestPayload
        };
        var decodedValue = await ISequenceMappingOperationsService.Request.DecodeSendSequenceOfStringAsync(
            request,
            default);
        Assert.That(decodedValue, Is.EqualTo(value));
    }

    [Test]
    public async Task Operation_returning_a_sequence_of_enum()
    {
        // Arrange
        var value = new MyEnum[] { MyEnum.Enum1, MyEnum.Enum2, MyEnum.Enum3 };
        PipeReader responsePayload = ISequenceMappingOperationsService.Response.EncodeReturnSequenceOfMyEnum(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        MyEnum[] decodedValue =
            await SequenceMappingOperationsProxy.Response.DecodeReturnSequenceOfMyEnumAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(decodedValue, Is.EqualTo(value));
    }

    [Test]
    public async Task Operation_sending_a_sequence_of_enum()
    {
        // Arrange
        var value = new MyEnum[] { MyEnum.Enum1, MyEnum.Enum2, MyEnum.Enum3 };

        // Act
        PipeReader requestPayload = SequenceMappingOperationsProxy.Request.EncodeSendSequenceOfMyEnum(value);

        // Assert
        using var request = new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
        {
            Payload = requestPayload
        };
        var decodedValue = await ISequenceMappingOperationsService.Request.DecodeSendSequenceOfMyEnumAsync(
            request,
            default);
        Assert.That(decodedValue, Is.EqualTo(value));
    }

    [Test]
    public async Task Operation_returning_a_sequence_of_structs()
    {
        // Arrange
        MyStruct[] value = [new MyStruct(0, 0), new MyStruct(1, 1), new MyStruct(2, 2)];
        PipeReader responsePayload = ISequenceMappingOperationsService.Response.EncodeReturnSequenceOfMyStruct(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        MyStruct[] decodedValue =
            await SequenceMappingOperationsProxy.Response.DecodeReturnSequenceOfMyStructAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(decodedValue, Is.EqualTo(value));
    }

    [Test]
    public async Task Operation_sending_a_sequence_of_structs()
    {
        // Arrange
        MyStruct[] value = [new MyStruct(0, 0), new MyStruct(1, 1), new MyStruct(2, 2)];

        // Act
        PipeReader requestPayload = SequenceMappingOperationsProxy.Request.EncodeSendSequenceOfMyStruct(value);

        // Assert
        using var request = new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
        {
            Payload = requestPayload
        };
        var decodedValue = await ISequenceMappingOperationsService.Request.DecodeSendSequenceOfMyStructAsync(
            request,
            default);
        Assert.That(decodedValue, Is.EqualTo(value));
    }

    [Test]
    public async Task Operation_returning_a_custom_sequence_of_int()
    {
        // Arrange
        var value = new int[] { 1, 2, 3 };
        PipeReader responsePayload = ISequenceMappingOperationsService.Response.EncodeReturnCustomSequenceOfInt(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        CustomSequence<int> decodedValue =
            await SequenceMappingOperationsProxy.Response.DecodeReturnCustomSequenceOfIntAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(decodedValue, Is.EqualTo(new CustomSequence<int>(value)));
    }

    [Test]
    public async Task Operation_sending_a_custom_sequence_of_int()
    {
        // Arrange
        var value = new CustomSequence<int>(new int[] { 1, 2, 3 });

        // Act
        PipeReader requestPayload = SequenceMappingOperationsProxy.Request.EncodeSendCustomSequenceOfInt(value);

        // Assert
        using var request = new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
        {
            Payload = requestPayload
        };
        var decodedValue = await ISequenceMappingOperationsService.Request.DecodeSendCustomSequenceOfIntAsync(
            request,
            default);
        Assert.That(decodedValue, Is.EqualTo(value));
    }

    [Test]
    public async Task Operation_returning_a_custom_sequence_of_string()
    {
        // Arrange
        var value = new string[] { "one", "two", "three" };
        PipeReader responsePayload =
            ISequenceMappingOperationsService.Response.EncodeReturnCustomSequenceOfString(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        CustomSequence<string> decodedValue =
            await SequenceMappingOperationsProxy.Response.DecodeReturnCustomSequenceOfStringAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(decodedValue, Is.EqualTo(new CustomSequence<string>(value)));
    }

    [Test]
    public async Task Operation_sending_a_custom_sequence_of_string()
    {
        // Arrange
        var value = new CustomSequence<string>(new string[] { "one", "two", "three" });

        // Act
        PipeReader requestPayload = SequenceMappingOperationsProxy.Request.EncodeSendCustomSequenceOfString(value);

        // Assert
        using var request = new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
        {
            Payload = requestPayload
        };
        var decodedValue =
            await ISequenceMappingOperationsService.Request.DecodeSendCustomSequenceOfStringAsync(request, default);
        Assert.That(decodedValue, Is.EqualTo(value));
    }

    [Test]
    public async Task Operation_returning_a_custom_sequence_of_enum()
    {
        // Arrange
        var value = new MyEnum[] { MyEnum.Enum1, MyEnum.Enum2, MyEnum.Enum3 };
        PipeReader responsePayload =
            ISequenceMappingOperationsService.Response.EncodeReturnCustomSequenceOfMyEnum(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        CustomSequence<MyEnum> decodedValue =
            await SequenceMappingOperationsProxy.Response.DecodeReturnCustomSequenceOfMyEnumAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(decodedValue, Is.EqualTo(new CustomSequence<MyEnum>(value)));
    }

    [Test]
    public async Task Operation_sending_a_custom_sequence_of_enum()
    {
        // Arrange
        var value = new CustomSequence<MyEnum>(
            new MyEnum[] { MyEnum.Enum1, MyEnum.Enum2, MyEnum.Enum3 });

        // Act
        PipeReader requestPayload = SequenceMappingOperationsProxy.Request.EncodeSendCustomSequenceOfMyEnum(value);

        // Assert
        using var request = new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
        {
            Payload = requestPayload
        };
        var decodedValue =
            await ISequenceMappingOperationsService.Request.DecodeSendCustomSequenceOfMyEnumAsync(request, default);
        Assert.That(decodedValue, Is.EqualTo(value));
    }

    [Test]
    public async Task Operation_returning_a_hash_set_of_int()
    {
        // Arrange
        var value = new HashSet<int> { 1, 2, 3 };
        PipeReader responsePayload = ISequenceMappingOperationsService.Response.EncodeReturnHashSetOfInt(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        HashSet<int> decodedValue =
            await SequenceMappingOperationsProxy.Response.DecodeReturnHashSetOfIntAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(decodedValue, Is.EqualTo(value));
    }

    [Test]
    public async Task Operation_sending_a_hash_set_of_int()
    {
        // Arrange
        var value = new HashSet<int> { 1, 2, 3 };

        // Act
        PipeReader requestPayload = SequenceMappingOperationsProxy.Request.EncodeSendHashSetOfInt(value);

        // Assert
        using var request = new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
        {
            Payload = requestPayload
        };
        var decodedValue = await ISequenceMappingOperationsService.Request.DecodeSendHashSetOfIntAsync(
            request,
            default);
        Assert.That(decodedValue, Is.EqualTo(value));
    }

    [Test]
    public async Task Struct_nested_sequence_parameter()
    {
        // Arrange
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
            Payload = SequenceMappingOperationsProxy.Request.EncodeOpStructNestedSequence(data)
        };

        // Act
        ValueTask<IList<IList<MyStruct>>[]> result =
            SequenceMappingOperationsProxy.Response.DecodeOpStructNestedSequenceAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(await result, Is.EqualTo(data));
    }

    [Test]
    public async Task Numeric_nested_sequence_parameter()
    {
        // Arrange
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
            Payload = SequenceMappingOperationsProxy.Request.EncodeOpNumericTypeNestedSequence(data)
        };

        // Act
        ValueTask<IList<IList<byte>>[]> result =
            SequenceMappingOperationsProxy.Response.DecodeOpNumericTypeNestedSequenceAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(await result, Is.EqualTo(data));
    }

    [Test]
    public async Task Return_and_out_parameter()
    {
        // Arrange
        PipeReader responsePayload = ISequenceMappingOperationsService.Response.EncodeOpReturnAndOut(
            new int[] { 1, 2, 3 },
            new int[] { 4, 5, 6 });
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        (CustomSequence<int> returnValue, CustomSequence<int> r1) =
            await SequenceMappingOperationsProxy.Response.DecodeOpReturnAndOutAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(returnValue, Is.EqualTo(new CustomSequence<int>(new int[] { 1, 2, 3 })));
        Assert.That(r1, Is.EqualTo(new CustomSequence<int>(new int[] { 4, 5, 6 })));
    }
}
