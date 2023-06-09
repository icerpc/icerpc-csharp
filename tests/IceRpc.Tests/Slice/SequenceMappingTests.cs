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
    public async Task Operation_returning_a_sequence_of_fixed_size_numeric()
    {
        // Arrange
        var value = new int[] { 1, 2, 3 };
        PipeReader responsePayload = ISequenceMappingOperationsService.Response.EncodeReturnSequenceOfInt32(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        int[] r =
            await SequenceMappingOperationsProxy.Response.DecodeReturnSequenceOfInt32Async(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(r, Is.EqualTo(value));
    }

    [Test]
    public void Operation_sending_a_sequence_of_fixed_size_numeric()
    {
        // Arrange
        var value = new int[] { 1, 2, 3 };

        // Act
        PipeReader requestPayload = SequenceMappingOperationsProxy.Request.EncodeSendSequenceOfInt32(value);

        // Assert
        Assert.That(
            async () => await ISequenceMappingOperationsService.Request.DecodeSendSequenceOfInt32Async(
                new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
                {
                    Payload = requestPayload
                },
                default),
            Throws.Nothing);
    }

    [Test]
    public async Task Operation_returning_a_sequence_of_string()
    {
        // Arrange
        var value = new string[] { "one", "two", "three" };
        PipeReader responsePayload = ISequenceMappingOperationsService.Response.EncodeReturnSequenceOfString(
            value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        string[] r =
            await SequenceMappingOperationsProxy.Response.DecodeReturnSequenceOfStringAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(r, Is.EqualTo(value));
    }

    [Test]
    public void Operation_sending_a_sequence_of_string()
    {
        // Act
        PipeReader requestPayload = SequenceMappingOperationsProxy.Request.EncodeSendSequenceOfString(
            new string[] { "one", "two", "three" });

        // Assert
        Assert.That(
            async () => await ISequenceMappingOperationsService.Request.DecodeSendSequenceOfStringAsync(
                new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
                {
                    Payload = requestPayload
                },
                default),
            Throws.Nothing);
    }

    [Test]
    public async Task Operation_returning_a_sequence_of_fixed_size_enum()
    {
        // Arrange
        var value = new MyFixedLengthEnum[]
        {
            MyFixedLengthEnum.SEnum1,
            MyFixedLengthEnum.SEnum2,
            MyFixedLengthEnum.SEnum3
        };
        PipeReader responsePayload =
            ISequenceMappingOperationsService.Response.EncodeReturnSequenceOfMyFixedLengthEnum(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        MyFixedLengthEnum[] r =
            await SequenceMappingOperationsProxy.Response.DecodeReturnSequenceOfMyFixedLengthEnumAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(r, Is.EqualTo(value));
    }

    [Test]
    public void Operation_sending_a_sequence_of_fixed_size_enum()
    {
        // Arrange
        var value = new MyFixedLengthEnum[]
        {
            MyFixedLengthEnum.SEnum1,
            MyFixedLengthEnum.SEnum2,
            MyFixedLengthEnum.SEnum3
        };

        // Act
        PipeReader requestPayload =
            SequenceMappingOperationsProxy.Request.EncodeSendSequenceOfMyFixedLengthEnum(value);

        // Assert
        Assert.That(
            async () => await ISequenceMappingOperationsService.Request.DecodeSendSequenceOfMyFixedLengthEnumAsync(
                new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
                {
                    Payload = requestPayload
                },
                default),
            Throws.Nothing);
    }

    [Test]
    public async Task Operation_returning_a_sequence_of_var_size_enum()
    {
        // Arrange
        var value = new MyVarSizeEnum[] { MyVarSizeEnum.Enum1, MyVarSizeEnum.Enum2, MyVarSizeEnum.Enum3 };
        PipeReader responsePayload =
            ISequenceMappingOperationsService.Response.EncodeReturnSequenceOfMyVarSizeEnum(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        MyVarSizeEnum[] r =
            await SequenceMappingOperationsProxy.Response.DecodeReturnSequenceOfMyVarSizeEnumAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(r, Is.EqualTo(value));
    }

    [Test]
    public void Operation_sending_a_sequence_of_var_size_enum()
    {
        // Arrange
        var value = new MyVarSizeEnum[]
        {
            MyVarSizeEnum.Enum1,
            MyVarSizeEnum.Enum2,
            MyVarSizeEnum.Enum3
        };

        // Act
        PipeReader requestPayload = SequenceMappingOperationsProxy.Request.EncodeSendSequenceOfMyVarSizeEnum(value);

        // Assert
        Assert.That(
            async () => await ISequenceMappingOperationsService.Request.DecodeSendSequenceOfMyVarSizeEnumAsync(
                new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
                {
                    Payload = requestPayload
                },
                default),
            Throws.Nothing);
    }

    [Test]
    public async Task Operation_returning_a_sequence_of_unchecked_enum()
    {
        // Arrange
        var value = new MyUncheckedEnum[] { MyUncheckedEnum.E1, MyUncheckedEnum.E2, MyUncheckedEnum.E3 };
        PipeReader responsePayload = ISequenceMappingOperationsService.Response.EncodeReturnSequenceOfMyUncheckedEnum(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        MyUncheckedEnum[] r =
            await SequenceMappingOperationsProxy.Response.DecodeReturnSequenceOfMyUncheckedEnumAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(r, Is.EqualTo(value));
    }

    [Test]
    public void Operation_sending_a_sequence_of_unchecked_enum()
    {
        // Arrange
        var value = new MyUncheckedEnum[]
        {
            MyUncheckedEnum.E1,
            MyUncheckedEnum.E2,
            MyUncheckedEnum.E3
        };

        // Act
        PipeReader requestPayload =
            SequenceMappingOperationsProxy.Request.EncodeSendSequenceOfMyUncheckedEnum(value);

        // Assert
        Assert.That(
            async () => await ISequenceMappingOperationsService.Request.DecodeSendSequenceOfMyUncheckedEnumAsync(
                new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
                {
                    Payload = requestPayload
                },
                default),
            Throws.Nothing);
    }

    [Test]
    public async Task Operation_returning_a_sequence_of_optional_fixed_size_numeric()
    {
        // Arrange
        var value = new int?[] { 1, null, 3 };
        PipeReader responsePayload =
            ISequenceMappingOperationsService.Response.EncodeReturnSequenceOfOptionalInt32(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        int?[] r =
            await SequenceMappingOperationsProxy.Response.DecodeReturnSequenceOfOptionalInt32Async(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(r, Is.EqualTo(value));
    }

    [Test]
    public void Operation_sending_a_sequence_of_optional_fixed_size_numeric()
    {
        // Arrange
        var value = new int?[] { 1, null, 3 };

        // Act
        PipeReader requestPayload = SequenceMappingOperationsProxy.Request.EncodeSendSequenceOfOptionalInt32(value);

        // Assert
        Assert.That(
            async () => await ISequenceMappingOperationsService.Request.DecodeSendSequenceOfOptionalInt32Async(
                new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
                {
                    Payload = requestPayload
                },
                default),
            Throws.Nothing);
    }

    [Test]
    public async Task Operation_returning_a_sequence_of_optional_string()
    {
        // Arrange
        var value = new string?[] { "one", null, "three" };
        PipeReader responsePayload = ISequenceMappingOperationsService.Response.EncodeReturnSequenceOfOptionalString(
            value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        string?[] r =
            await SequenceMappingOperationsProxy.Response.DecodeReturnSequenceOfOptionalStringAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(r, Is.EqualTo(value));
    }

    [Test]
    public void Operation_sending_a_sequence_of_optional_string()
    {
        // Act
        PipeReader requestPayload = SequenceMappingOperationsProxy.Request.EncodeSendSequenceOfOptionalString(
            new string?[] { "one", null, "three" });

        // Assert
        Assert.That(
            async () => await ISequenceMappingOperationsService.Request.DecodeSendSequenceOfOptionalStringAsync(
                new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
                {
                    Payload = requestPayload
                },
                default),
            Throws.Nothing);
    }

    [Test]
    public async Task Operation_returning_a_sequence_of_optional_fixed_size_enum()
    {
        // Arrange
        var value = new MyFixedLengthEnum?[] { MyFixedLengthEnum.SEnum1, null, MyFixedLengthEnum.SEnum3 };
        PipeReader responsePayload =
            ISequenceMappingOperationsService.Response.EncodeReturnSequenceOfOptionalMyFixedLengthEnum(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        MyFixedLengthEnum?[] r =
            await SequenceMappingOperationsProxy.Response.DecodeReturnSequenceOfOptionalMyFixedLengthEnumAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(r, Is.EqualTo(value));
    }

    [Test]
    public void Operation_sending_a_sequence_of_optional_fixed_size_enum()
    {
        // Act
        PipeReader requestPayload =
            SequenceMappingOperationsProxy.Request.EncodeSendSequenceOfOptionalMyFixedLengthEnum(
                new MyFixedLengthEnum?[] { MyFixedLengthEnum.SEnum1, null, MyFixedLengthEnum.SEnum3 });

        // Assert
        Assert.That(
            async () => await ISequenceMappingOperationsService.Request.DecodeSendSequenceOfOptionalMyFixedLengthEnumAsync(
                new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
                {
                    Payload = requestPayload
                },
                default),
            Throws.Nothing);
    }

    [Test]
    public async Task Operation_returning_a_sequence_of_optional_var_size_enum()
    {
        // Arrange
        var value = new MyVarSizeEnum?[] { MyVarSizeEnum.Enum1, null, MyVarSizeEnum.Enum3 };
        PipeReader responsePayload =
            ISequenceMappingOperationsService.Response.EncodeReturnSequenceOfOptionalMyVarSizeEnum(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        MyVarSizeEnum?[] r =
            await SequenceMappingOperationsProxy.Response.DecodeReturnSequenceOfOptionalMyVarSizeEnumAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(r, Is.EqualTo(value));
    }

    [Test]
    public void Operation_sending_a_sequence_of_optional_var_size_enum()
    {
        // Act
        PipeReader requestPayload =
            SequenceMappingOperationsProxy.Request.EncodeSendSequenceOfOptionalMyVarSizeEnum(
                new MyVarSizeEnum?[] { MyVarSizeEnum.Enum1, null, MyVarSizeEnum.Enum3 });

        // Assert
        Assert.That(
            async () => await ISequenceMappingOperationsService.Request.DecodeSendSequenceOfOptionalMyVarSizeEnumAsync(
                new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
                {
                    Payload = requestPayload
                },
                default),
            Throws.Nothing);
    }

    [Test]
    public async Task Operation_returning_a_sequence_of_optional_unchecked_enum()
    {
        // Arrange
        var value = new MyUncheckedEnum?[] { MyUncheckedEnum.E1, null, MyUncheckedEnum.E2 };
        PipeReader responsePayload =
            ISequenceMappingOperationsService.Response.EncodeReturnSequenceOfOptionalMyUncheckedEnum(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        MyUncheckedEnum?[] r =
            await SequenceMappingOperationsProxy.Response.DecodeReturnSequenceOfOptionalMyUncheckedEnumAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(r, Is.EqualTo(value));
    }

    [Test]
    public void Operation_sending_a_sequence_of_optional_unchecked_enum()
    {
        // Act
        PipeReader requestPayload =
            SequenceMappingOperationsProxy.Request.EncodeSendSequenceOfOptionalMyUncheckedEnum(
                new MyUncheckedEnum?[] { MyUncheckedEnum.E1, null, MyUncheckedEnum.E3 });

        // Assert
        Assert.That(
            async () => await ISequenceMappingOperationsService.Request.DecodeSendSequenceOfOptionalMyUncheckedEnumAsync(
                new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
                {
                    Payload = requestPayload
                },
                default),
            Throws.Nothing);
    }

    [Test]
    public async Task Operation_returning_a_custom_sequence_of_fixed_size_numeric()
    {
        // Arrange
        var value = new int[] { 1, 2, 3 };
        PipeReader responsePayload = ISequenceMappingOperationsService.Response.EncodeReturnCustomSequenceOfInt32(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        CustomSequence<int> r =
            await SequenceMappingOperationsProxy.Response.DecodeReturnCustomSequenceOfInt32Async(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(r, Is.EqualTo(new CustomSequence<int>(value)));
    }

    [Test]
    public void Operation_sending_a_custom_sequence_of_fixed_size_numeric()
    {
        // Act
        PipeReader requestPayload = SequenceMappingOperationsProxy.Request.EncodeSendCustomSequenceOfInt32(
            new CustomSequence<int>(new int[] { 1, 2, 3 }));

        // Assert
        Assert.That(
            async () => await ISequenceMappingOperationsService.Request.DecodeSendCustomSequenceOfInt32Async(
                new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
                {
                    Payload = requestPayload
                },
                default),
            Throws.Nothing);
    }

    [Test]
    public async Task Operation_returning_a_custom_sequence_of_string()
    {
        // Arrange
        var value = new string[] { "one", "two", "three" };
        PipeReader responsePayload = ISequenceMappingOperationsService.Response.EncodeReturnCustomSequenceOfString(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        CustomSequence<string> r =
            await SequenceMappingOperationsProxy.Response.DecodeReturnCustomSequenceOfStringAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(r, Is.EqualTo(new CustomSequence<string>(value)));
    }

    [Test]
    public void Operation_sending_a_custom_sequence_of_string()
    {
        // Act
        PipeReader requestPayload = SequenceMappingOperationsProxy.Request.EncodeSendCustomSequenceOfString(
            new CustomSequence<string>(new string[] { "one", "two", "three" }));

        // Assert
        Assert.That(
            async () => await ISequenceMappingOperationsService.Request.DecodeSendCustomSequenceOfStringAsync(
                new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
                {
                    Payload = requestPayload
                },
                default),
            Throws.Nothing);
    }

    [Test]
    public async Task Operation_returning_a_custom_sequence_of_fixed_size_enum_()
    {
        // Arrange
        var value = new MyFixedLengthEnum[]
        {
            MyFixedLengthEnum.SEnum1,
            MyFixedLengthEnum.SEnum2,
            MyFixedLengthEnum.SEnum3
        };
        PipeReader responsePayload =
            ISequenceMappingOperationsService.Response.EncodeReturnCustomSequenceOfMyFixedLengthEnum(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        CustomSequence<MyFixedLengthEnum> r =
            await SequenceMappingOperationsProxy.Response.DecodeReturnCustomSequenceOfMyFixedLengthEnumAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(r, Is.EqualTo(new CustomSequence<MyFixedLengthEnum>(value)));
    }

    [Test]
    public void Operation_sending_a_custom_sequence_of_fixed_size_enum()
    {
        // Act
        PipeReader requestPayload = SequenceMappingOperationsProxy.Request.EncodeSendCustomSequenceOfMyFixedLengthEnum(
            new CustomSequence<MyFixedLengthEnum>(
                new MyFixedLengthEnum[]
                {
                    MyFixedLengthEnum.SEnum1,
                    MyFixedLengthEnum.SEnum2,
                    MyFixedLengthEnum.SEnum3
                }));

        // Assert
        Assert.That(
            async () => await ISequenceMappingOperationsService.Request.DecodeSendCustomSequenceOfMyFixedLengthEnumAsync(
                new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
                {
                    Payload = requestPayload
                },
                default),
            Throws.Nothing);
    }

    [Test]
    public async Task Operation_returning_a_custom_sequence_of_var_size_enum()
    {
        // Arrange
        var value = new MyVarSizeEnum[] { MyVarSizeEnum.Enum1, MyVarSizeEnum.Enum2, MyVarSizeEnum.Enum3 };
        PipeReader responsePayload =
            ISequenceMappingOperationsService.Response.EncodeReturnCustomSequenceOfMyVarSizeEnum(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        CustomSequence<MyVarSizeEnum> r =
            await SequenceMappingOperationsProxy.Response.DecodeReturnCustomSequenceOfMyVarSizeEnumAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(r, Is.EqualTo(new CustomSequence<MyVarSizeEnum>(value)));
    }

    [Test]
    public void Operation_sending_a_custom_sequence_of_var_size_enum()
    {
        // Act
        PipeReader requestPayload = SequenceMappingOperationsProxy.Request.EncodeSendCustomSequenceOfMyVarSizeEnum(
            new CustomSequence<MyVarSizeEnum>(
                new MyVarSizeEnum[]
                {
                    MyVarSizeEnum.Enum1,
                    MyVarSizeEnum.Enum2,
                    MyVarSizeEnum.Enum3
                }));

        // Assert
        Assert.That(
            async () => await ISequenceMappingOperationsService.Request.DecodeSendCustomSequenceOfMyVarSizeEnumAsync(
                new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
                {
                    Payload = requestPayload
                },
                default),
            Throws.Nothing);
    }

    [Test]
    public async Task Operation_returning_a_custom_sequence_of_unchecked_enum()
    {
        // Arrange
        var value = new MyUncheckedEnum[] { MyUncheckedEnum.E1, MyUncheckedEnum.E2, MyUncheckedEnum.E3 };
        PipeReader responsePayload =
            ISequenceMappingOperationsService.Response.EncodeReturnCustomSequenceOfMyUncheckedEnum(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        CustomSequence<MyUncheckedEnum> r =
            await SequenceMappingOperationsProxy.Response.DecodeReturnCustomSequenceOfMyUncheckedEnumAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(r, Is.EqualTo(new CustomSequence<MyUncheckedEnum>(value)));
    }

    [Test]
    public void Operation_sending_a_custom_sequence_of_unchecked_enum()
    {
        // Act
        PipeReader requestPayload = SequenceMappingOperationsProxy.Request.EncodeSendCustomSequenceOfMyUncheckedEnum(
            new CustomSequence<MyUncheckedEnum>(
                new MyUncheckedEnum[]
                {
                    MyUncheckedEnum.E1,
                    MyUncheckedEnum.E2,
                    MyUncheckedEnum.E3
                }));

        // Assert
        Assert.That(
            async () => await ISequenceMappingOperationsService.Request.DecodeSendCustomSequenceOfMyUncheckedEnumAsync(
                new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
                {
                    Payload = requestPayload
                },
                default),
            Throws.Nothing);
    }

    [Test]
    public async Task Operation_returning_a_hash_set_of_fixed_size_numeric()
    {
        // Arrange
        var value = new HashSet<int> { 1, 2, 3 };
        PipeReader responsePayload = ISequenceMappingOperationsService.Response.EncodeReturnHashSetOfInt32(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        HashSet<int> r =
            await SequenceMappingOperationsProxy.Response.DecodeReturnHashSetOfInt32Async(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(r, Is.EqualTo(value));
    }

    [Test]
    public void Operation_sending_a_hash_set_of_fixed_size_numeric()
    {
        // Arrange
        var value = new HashSet<int> { 1, 2, 3 };

        // Act
        PipeReader requestPayload = SequenceMappingOperationsProxy.Request.EncodeSendHashSetOfInt32(value);

        // Assert
        Assert.That(
            async () => await ISequenceMappingOperationsService.Request.DecodeSendHashSetOfInt32Async(
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
            Payload = SequenceMappingOperationsProxy.Request.EncodeOpStructNestedSequence(data)
        };

        ValueTask<IList<IList<MyStruct>>[]> result =
            SequenceMappingOperationsProxy.Response.DecodeOpStructNestedSequenceAsync(
                response,
                request,
                InvalidProxy.Instance,
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
            Payload = SequenceMappingOperationsProxy.Request.EncodeOpNumericTypeNestedSequence(data)
        };

        ValueTask<IList<IList<byte>>[]> result =
            SequenceMappingOperationsProxy.Response.DecodeOpNumericTypeNestedSequenceAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        Assert.That(await result, Is.EqualTo(data));
    }

    [Test]
    public async Task Return_tuple_with_elements()
    {
        PipeReader responsePayload = ISequenceMappingOperationsService.Response.EncodeOpReturnTuple(
            new int[] { 1, 2, 3 },
            new int[] { 1, 2, 3 });
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        (CustomSequence<int> r1, CustomSequence<int> r2) =
            await SequenceMappingOperationsProxy.Response.DecodeOpReturnTupleAsync(
                response,
                request,
                new GenericProxy { Invoker = NotImplementedInvoker.Instance, ServiceAddress = null! },
                default);

        Assert.That(r1, Is.EqualTo(new CustomSequence<int>(new int[] { 1, 2, 3 })));
        Assert.That(r2, Is.EqualTo(new CustomSequence<int>(new int[] { 1, 2, 3 })));
    }
}
