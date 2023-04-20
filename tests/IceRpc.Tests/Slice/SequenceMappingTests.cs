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
        PipeReader responsePayload = ISequenceMappingOperationsService.Response.ReturnSequenceOfInt32(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        int[] r =
            await SequenceMappingOperationsProxy.Response.ReturnSequenceOfInt32Async(
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
        PipeReader requestPayload = SequenceMappingOperationsProxy.Request.SendSequenceOfInt32(value);

        // Assert
        Assert.That(
            async () => await ISequenceMappingOperationsService.Request.SendSequenceOfInt32Async(
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
        PipeReader responsePayload = ISequenceMappingOperationsService.Response.ReturnSequenceOfString(
            value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        string[] r =
            await SequenceMappingOperationsProxy.Response.ReturnSequenceOfStringAsync(
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
        PipeReader requestPayload = SequenceMappingOperationsProxy.Request.SendSequenceOfString(
            new string[] { "one", "two", "three" });

        // Assert
        Assert.That(
            async () => await ISequenceMappingOperationsService.Request.SendSequenceOfStringAsync(
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
            ISequenceMappingOperationsService.Response.ReturnSequenceOfMyFixedLengthEnum(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        MyFixedLengthEnum[] r =
            await SequenceMappingOperationsProxy.Response.ReturnSequenceOfMyFixedLengthEnumAsync(
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
            SequenceMappingOperationsProxy.Request.SendSequenceOfMyFixedLengthEnum(value);

        // Assert
        Assert.That(
            async () => await ISequenceMappingOperationsService.Request.SendSequenceOfMyFixedLengthEnumAsync(
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
            ISequenceMappingOperationsService.Response.ReturnSequenceOfMyVarSizeEnum(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        MyVarSizeEnum[] r =
            await SequenceMappingOperationsProxy.Response.ReturnSequenceOfMyVarSizeEnumAsync(
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
        PipeReader requestPayload = SequenceMappingOperationsProxy.Request.SendSequenceOfMyVarSizeEnum(value);

        // Assert
        Assert.That(
            async () => await ISequenceMappingOperationsService.Request.SendSequenceOfMyVarSizeEnumAsync(
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
        PipeReader responsePayload = ISequenceMappingOperationsService.Response.ReturnSequenceOfMyUncheckedEnum(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        MyUncheckedEnum[] r =
            await SequenceMappingOperationsProxy.Response.ReturnSequenceOfMyUncheckedEnumAsync(
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
            SequenceMappingOperationsProxy.Request.SendSequenceOfMyUncheckedEnum(value);

        // Assert
        Assert.That(
            async () => await ISequenceMappingOperationsService.Request.SendSequenceOfMyUncheckedEnumAsync(
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
            ISequenceMappingOperationsService.Response.ReturnSequenceOfOptionalInt32(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        int?[] r =
            await SequenceMappingOperationsProxy.Response.ReturnSequenceOfOptionalInt32Async(
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
        PipeReader requestPayload = SequenceMappingOperationsProxy.Request.SendSequenceOfOptionalInt32(value);

        // Assert
        Assert.That(
            async () => await ISequenceMappingOperationsService.Request.SendSequenceOfOptionalInt32Async(
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
        PipeReader responsePayload = ISequenceMappingOperationsService.Response.ReturnSequenceOfOptionalString(
            value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        string?[] r =
            await SequenceMappingOperationsProxy.Response.ReturnSequenceOfOptionalStringAsync(
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
        PipeReader requestPayload = SequenceMappingOperationsProxy.Request.SendSequenceOfOptionalString(
            new string?[] { "one", null, "three" });

        // Assert
        Assert.That(
            async () => await ISequenceMappingOperationsService.Request.SendSequenceOfOptionalStringAsync(
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
            ISequenceMappingOperationsService.Response.ReturnSequenceOfOptionalMyFixedLengthEnum(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        MyFixedLengthEnum?[] r =
            await SequenceMappingOperationsProxy.Response.ReturnSequenceOfOptionalMyFixedLengthEnumAsync(
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
            SequenceMappingOperationsProxy.Request.SendSequenceOfOptionalMyFixedLengthEnum(
                new MyFixedLengthEnum?[] { MyFixedLengthEnum.SEnum1, null, MyFixedLengthEnum.SEnum3 });

        // Assert
        Assert.That(
            async () => await ISequenceMappingOperationsService.Request.SendSequenceOfOptionalMyFixedLengthEnumAsync(
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
            ISequenceMappingOperationsService.Response.ReturnSequenceOfOptionalMyVarSizeEnum(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        MyVarSizeEnum?[] r =
            await SequenceMappingOperationsProxy.Response.ReturnSequenceOfOptionalMyVarSizeEnumAsync(
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
            SequenceMappingOperationsProxy.Request.SendSequenceOfOptionalMyVarSizeEnum(
                new MyVarSizeEnum?[] { MyVarSizeEnum.Enum1, null, MyVarSizeEnum.Enum3 });

        // Assert
        Assert.That(
            async () => await ISequenceMappingOperationsService.Request.SendSequenceOfOptionalMyVarSizeEnumAsync(
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
            ISequenceMappingOperationsService.Response.ReturnSequenceOfOptionalMyUncheckedEnum(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        MyUncheckedEnum?[] r =
            await SequenceMappingOperationsProxy.Response.ReturnSequenceOfOptionalMyUncheckedEnumAsync(
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
            SequenceMappingOperationsProxy.Request.SendSequenceOfOptionalMyUncheckedEnum(
                new MyUncheckedEnum?[] { MyUncheckedEnum.E1, null, MyUncheckedEnum.E3 });

        // Assert
        Assert.That(
            async () => await ISequenceMappingOperationsService.Request.SendSequenceOfOptionalMyUncheckedEnumAsync(
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
        PipeReader responsePayload = ISequenceMappingOperationsService.Response.ReturnCustomSequenceOfInt32(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        CustomSequence<int> r =
            await SequenceMappingOperationsProxy.Response.ReturnCustomSequenceOfInt32Async(
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
        PipeReader requestPayload = SequenceMappingOperationsProxy.Request.SendCustomSequenceOfInt32(
            new CustomSequence<int>(new int[] { 1, 2, 3 }));

        // Assert
        Assert.That(
            async () => await ISequenceMappingOperationsService.Request.SendCustomSequenceOfInt32Async(
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
        PipeReader responsePayload = ISequenceMappingOperationsService.Response.ReturnCustomSequenceOfString(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        CustomSequence<string> r =
            await SequenceMappingOperationsProxy.Response.ReturnCustomSequenceOfStringAsync(
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
        PipeReader requestPayload = SequenceMappingOperationsProxy.Request.SendCustomSequenceOfString(
            new CustomSequence<string>(new string[] { "one", "two", "three" }));

        // Assert
        Assert.That(
            async () => await ISequenceMappingOperationsService.Request.SendCustomSequenceOfStringAsync(
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
            ISequenceMappingOperationsService.Response.ReturnCustomSequenceOfMyFixedLengthEnum(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        CustomSequence<MyFixedLengthEnum> r =
            await SequenceMappingOperationsProxy.Response.ReturnCustomSequenceOfMyFixedLengthEnumAsync(
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
        PipeReader requestPayload = SequenceMappingOperationsProxy.Request.SendCustomSequenceOfMyFixedLengthEnum(
            new CustomSequence<MyFixedLengthEnum>(
                new MyFixedLengthEnum[]
                {
                    MyFixedLengthEnum.SEnum1,
                    MyFixedLengthEnum.SEnum2,
                    MyFixedLengthEnum.SEnum3
                }));

        // Assert
        Assert.That(
            async () => await ISequenceMappingOperationsService.Request.SendCustomSequenceOfMyFixedLengthEnumAsync(
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
            ISequenceMappingOperationsService.Response.ReturnCustomSequenceOfMyVarSizeEnum(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        CustomSequence<MyVarSizeEnum> r =
            await SequenceMappingOperationsProxy.Response.ReturnCustomSequenceOfMyVarSizeEnumAsync(
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
        PipeReader requestPayload = SequenceMappingOperationsProxy.Request.SendCustomSequenceOfMyVarSizeEnum(
            new CustomSequence<MyVarSizeEnum>(
                new MyVarSizeEnum[]
                {
                    MyVarSizeEnum.Enum1,
                    MyVarSizeEnum.Enum2,
                    MyVarSizeEnum.Enum3
                }));

        // Assert
        Assert.That(
            async () => await ISequenceMappingOperationsService.Request.SendCustomSequenceOfMyVarSizeEnumAsync(
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
            ISequenceMappingOperationsService.Response.ReturnCustomSequenceOfMyUncheckedEnum(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        CustomSequence<MyUncheckedEnum> r =
            await SequenceMappingOperationsProxy.Response.ReturnCustomSequenceOfMyUncheckedEnumAsync(
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
        PipeReader requestPayload = SequenceMappingOperationsProxy.Request.SendCustomSequenceOfMyUncheckedEnum(
            new CustomSequence<MyUncheckedEnum>(
                new MyUncheckedEnum[]
                {
                    MyUncheckedEnum.E1,
                    MyUncheckedEnum.E2,
                    MyUncheckedEnum.E3
                }));

        // Assert
        Assert.That(
            async () => await ISequenceMappingOperationsService.Request.SendCustomSequenceOfMyUncheckedEnumAsync(
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
            Payload = SequenceMappingOperationsProxy.Request.OpStructNestedSequence(data)
        };

        ValueTask<IList<IList<MyStruct>>[]> result =
            SequenceMappingOperationsProxy.Response.OpStructNestedSequenceAsync(
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
            Payload = SequenceMappingOperationsProxy.Request.OpNumericTypeNestedSequence(data)
        };

        ValueTask<IList<IList<byte>>[]> result =
            SequenceMappingOperationsProxy.Response.OpNumericTypeNestedSequenceAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        Assert.That(await result, Is.EqualTo(data));
    }

    [Test]
    public async Task Return_tuple_with_elements()
    {
        PipeReader responsePayload = ISequenceMappingOperationsService.Response.OpReturnTuple(
            new int[] { 1, 2, 3 },
            new int[] { 1, 2, 3 });
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        (CustomSequence<int> r1, CustomSequence<int> r2) =
            await SequenceMappingOperationsProxy.Response.OpReturnTupleAsync(
                response,
                request,
                new GenericProxy { Invoker = NotImplementedInvoker.Instance, ServiceAddress = null! },
                default);

        Assert.That(r1, Is.EqualTo(new CustomSequence<int>(new int[] { 1, 2, 3 })));
        Assert.That(r2, Is.EqualTo(new CustomSequence<int>(new int[] { 1, 2, 3 })));
    }
}
