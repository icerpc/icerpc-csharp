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
    public async Task Sequence_of_int32_return()
    {
        // Arrange
        var value = new int[] { 1, 2, 3 };
        PipeReader responsePayload = ISequenceMappingOperationsService.Response.OpSequenceOfInt32Return(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        int[] r =
            await SequenceMappingOperationsProxy.Response.OpSequenceOfInt32ReturnAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(r, Is.EqualTo(value));
    }

    [Test]
    public void Sequence_of_int32_parameter()
    {
        // Arrange
        var value = new int[] { 1, 2, 3 };

        // Act
        PipeReader requestPayload = SequenceMappingOperationsProxy.Request.OpSequenceOfInt32Parameter(value);

        // Assert
        Assert.That(
            async () => await ISequenceMappingOperationsService.Request.OpSequenceOfInt32ParameterAsync(
                new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
                {
                    Payload = requestPayload
                },
                default),
            Throws.Nothing);
    }

    [Test]
    public async Task Sequence_of_string_return()
    {
        // Arrange
        var value = new string[] { "one", "two", "three" };
        PipeReader responsePayload = ISequenceMappingOperationsService.Response.OpSequenceOfStringReturn(
            value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        string[] r =
            await SequenceMappingOperationsProxy.Response.OpSequenceOfStringReturnAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(r, Is.EqualTo(value));
    }

    [Test]
    public void Sequence_of_string_parameter()
    {
        // Act
        PipeReader requestPayload = SequenceMappingOperationsProxy.Request.OpCustomSequenceOfStringParameter(
            new string[] { "one", "two", "three" });

        // Assert
        Assert.That(
            async () => await ISequenceMappingOperationsService.Request.OpCustomSequenceOfStringParameterAsync(
                new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
                {
                    Payload = requestPayload
                },
                default),
            Throws.Nothing);
    }

    [Test]
    public async Task Sequence_of_fixed_lenght_enum_return()
    {
        // Arrange
        var value = new MyFixedLengthEnum[]
        {
            MyFixedLengthEnum.SEnum1,
            MyFixedLengthEnum.SEnum2,
            MyFixedLengthEnum.SEnum3
        };
        PipeReader responsePayload =
            ISequenceMappingOperationsService.Response.OpSequenceOfMyFixedLengthEnumReturn(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        MyFixedLengthEnum[] r =
            await SequenceMappingOperationsProxy.Response.OpSequenceOfMyFixedLengthEnumReturnAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(r, Is.EqualTo(value));
    }

    [Test]
    public void Sequence_of_fixed_lenght_enum_parameter()
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
            SequenceMappingOperationsProxy.Request.OpSequenceOfMyFixedLengthEnumParameter(value);

        // Assert
        Assert.That(
            async () => await ISequenceMappingOperationsService.Request.OpSequenceOfMyFixedLengthEnumParameterAsync(
                new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
                {
                    Payload = requestPayload
                },
                default),
            Throws.Nothing);
    }

    [Test]
    public async Task Sequence_of_var_lenght_enum_return()
    {
        // Arrange
        var value = new MyVarLengthEnum[] { MyVarLengthEnum.Enum1, MyVarLengthEnum.Enum2, MyVarLengthEnum.Enum3 };
        PipeReader responsePayload =
            ISequenceMappingOperationsService.Response.OpCustomSequenceOfMyVarLengthEnumReturn(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        MyVarLengthEnum[] r =
            await SequenceMappingOperationsProxy.Response.OpSequenceOfMyVarLengthEnumReturnAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(r, Is.EqualTo(value));
    }

    [Test]
    public void Sequence_of_var_lenght_enum_parameter()
    {
        // Arrange
        var value = new MyVarLengthEnum[]
        {
            MyVarLengthEnum.Enum1,
            MyVarLengthEnum.Enum2,
            MyVarLengthEnum.Enum3
        };

        // Act
        PipeReader requestPayload = SequenceMappingOperationsProxy.Request.OpSequenceOfMyVarLengthEnumParameter(value);

        // Assert
        Assert.That(
            async () => await ISequenceMappingOperationsService.Request.OpSequenceOfMyVarLengthEnumParameterAsync(
                new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
                {
                    Payload = requestPayload
                },
                default),
            Throws.Nothing);
    }

    [Test]
    public async Task Sequence_of_unchecked_enum_return()
    {
        // Arrange
        var value = new MyUncheckedEnum[] { MyUncheckedEnum.E1, MyUncheckedEnum.E2, MyUncheckedEnum.E3 };
        PipeReader responsePayload = ISequenceMappingOperationsService.Response.OpSequenceOfMyUncheckedEnumReturn(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        MyUncheckedEnum[] r =
            await SequenceMappingOperationsProxy.Response.OpSequenceOfMyUncheckedEnumReturnAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(r, Is.EqualTo(value));
    }

    [Test]
    public void Sequence_of_unchecked_enum_parameter()
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
            SequenceMappingOperationsProxy.Request.OpSequenceOfMyUncheckedEnumParameter(value);

        // Assert
        Assert.That(
            async () => await ISequenceMappingOperationsService.Request.OpSequenceOfMyUncheckedEnumParameterAsync(
                new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
                {
                    Payload = requestPayload
                },
                default),
            Throws.Nothing);
    }

    [Test]
    public async Task Sequence_of_optional_int32_return()
    {
        // Arrange
        var value = new int?[] { 1, null, 3 };
        PipeReader responsePayload =
            ISequenceMappingOperationsService.Response.OpSequenceOfOptionalInt32Return(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        int?[] r =
            await SequenceMappingOperationsProxy.Response.OpSequenceOfOptionalInt32ReturnAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(r, Is.EqualTo(value));
    }

    [Test]
    public void Sequence_of_optional_int32_parameter()
    {
        // Arrange
        var value = new int?[] { 1, null, 3 };

        // Act
        PipeReader requestPayload = SequenceMappingOperationsProxy.Request.OpSequenceOfOptionalInt32Parameter(value);

        // Assert
        Assert.That(
            async () => await ISequenceMappingOperationsService.Request.OpSequenceOfOptionalInt32ParameterAsync(
                new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
                {
                    Payload = requestPayload
                },
                default),
            Throws.Nothing);
    }

    [Test]
    public async Task Sequence_of_optional_string_return()
    {
        // Arrange
        var value = new string?[] { "one", null, "three" };
        PipeReader responsePayload = ISequenceMappingOperationsService.Response.OpSequenceOfOptionalStringReturn(
            value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        string?[] r =
            await SequenceMappingOperationsProxy.Response.OpSequenceOfOptionalStringReturnAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(r, Is.EqualTo(value));
    }

    [Test]
    public void Sequence_of_optional_string_parameter()
    {
        // Act
        PipeReader requestPayload = SequenceMappingOperationsProxy.Request.OpSequenceOfOptionalStringParameter(
            new string?[] { "one", null, "three" });

        // Assert
        Assert.That(
            async () => await ISequenceMappingOperationsService.Request.OpSequenceOfOptionalStringParameterAsync(
                new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
                {
                    Payload = requestPayload
                },
                default),
            Throws.Nothing);
    }

    [Test]
    public async Task Sequence_of_optional_fixed_lenght_enum_return()
    {
        // Arrange
        var value = new MyFixedLengthEnum?[] { MyFixedLengthEnum.SEnum1, null, MyFixedLengthEnum.SEnum3 };
        PipeReader responsePayload =
            ISequenceMappingOperationsService.Response.OpSequenceOfOptionalMyFixedLengthEnumReturn(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        MyFixedLengthEnum?[] r =
            await SequenceMappingOperationsProxy.Response.OpSequenceOfOptionalMyFixedLengthEnumReturnAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(r, Is.EqualTo(value));
    }

    [Test]
    public void Sequence_of_optional_fixed_length_enum_parameter()
    {
        // Act
        PipeReader requestPayload = 
            SequenceMappingOperationsProxy.Request.OpSequenceOfOptionalMyFixedLengthEnumParameter(
                new MyFixedLengthEnum?[] { MyFixedLengthEnum.SEnum1, null, MyFixedLengthEnum.SEnum3 });

        // Assert
        Assert.That(
            async () => await ISequenceMappingOperationsService.Request.OpSequenceOfOptionalMyFixedLengthEnumParameterAsync(
                new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
                {
                    Payload = requestPayload
                },
                default),
            Throws.Nothing);
    }

    [Test]
    public async Task Sequence_of_optional_var_lenght_enum_return()
    {
        // Arrange
        var value = new MyVarLengthEnum?[] { MyVarLengthEnum.Enum1, null, MyVarLengthEnum.Enum3 };
        PipeReader responsePayload =
            ISequenceMappingOperationsService.Response.OpSequenceOfOptionalMyVarLengthEnumReturn(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        MyVarLengthEnum?[] r =
            await SequenceMappingOperationsProxy.Response.OpSequenceOfOptionalMyVarLengthEnumReturnAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(r, Is.EqualTo(value));
    }

    [Test]
    public void Sequence_of_optional_var_length_enum_parameter()
    {
        // Act
        PipeReader requestPayload =
            SequenceMappingOperationsProxy.Request.OpSequenceOfOptionalMyVarLengthEnumParameter(
                new MyVarLengthEnum?[] { MyVarLengthEnum.Enum1, null, MyVarLengthEnum.Enum3 });

        // Assert
        Assert.That(
            async () => await ISequenceMappingOperationsService.Request.OpSequenceOfOptionalMyVarLengthEnumParameterAsync(
                new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
                {
                    Payload = requestPayload
                },
                default),
            Throws.Nothing);
    }

    [Test]
    public async Task Sequence_of_optional_unchecked_enum_return()
    {
        // Arrange
        var value = new MyUncheckedEnum?[] { MyUncheckedEnum.E1, null, MyUncheckedEnum.E2 };
        PipeReader responsePayload =
            ISequenceMappingOperationsService.Response.OpSequenceOfOptionalMyUncheckedEnumReturn(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        MyUncheckedEnum?[] r =
            await SequenceMappingOperationsProxy.Response.OpSequenceOfOptionalMyUncheckedEnumReturnAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(r, Is.EqualTo(value));
    }

    [Test]
    public void Sequence_of_optional_unchecked_enum_parameter()
    {
        // Act
        PipeReader requestPayload =
            SequenceMappingOperationsProxy.Request.OpSequenceOfOptionalMyUncheckedEnumParameter(
                new MyUncheckedEnum?[] { MyUncheckedEnum.E1, null, MyUncheckedEnum.E3 });

        // Assert
        Assert.That(
            async () => await ISequenceMappingOperationsService.Request.OpSequenceOfOptionalMyUncheckedEnumParameterAsync(
                new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
                {
                    Payload = requestPayload
                },
                default),
            Throws.Nothing);
    }

    [Test]
    public async Task Custom_sequence_of_int32_return()
    {
        // Arrange
        var value = new int[] { 1, 2, 3 };
        PipeReader responsePayload = ISequenceMappingOperationsService.Response.OpCustomSequenceOfInt32Return(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        CustomSequence<int> r =
            await SequenceMappingOperationsProxy.Response.OpCustomSequenceOfInt32ReturnAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(r, Is.EqualTo(new CustomSequence<int>(value)));
    }

    [Test]
    public void Custom_sequence_of_int32_parameter()
    {
        // Act
        PipeReader requestPayload = SequenceMappingOperationsProxy.Request.OpCustomSequenceOfInt32Parameter(
            new CustomSequence<int>(new int[] { 1, 2, 3 }));

        // Assert
        Assert.That(
            async () => await ISequenceMappingOperationsService.Request.OpCustomSequenceOfInt32ParameterAsync(
                new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
                {
                    Payload = requestPayload
                },
                default),
            Throws.Nothing);
    }

    [Test]
    public async Task Custom_sequence_of_string_return()
    {
        // Arrange
        var value = new string[] { "one", "two", "three" };
        PipeReader responsePayload = ISequenceMappingOperationsService.Response.OpCustomSequenceOfStringReturn(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        CustomSequence<string> r =
            await SequenceMappingOperationsProxy.Response.OpCustomSequenceOfStringReturnAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(r, Is.EqualTo(new CustomSequence<string>(value)));
    }

    [Test]
    public void Custom_sequence_of_string_parameter()
    {
        // Act
        PipeReader requestPayload = SequenceMappingOperationsProxy.Request.OpCustomSequenceOfStringParameter(
            new CustomSequence<string>(new string[] { "one", "two", "three" }));

        // Assert
        Assert.That(
            async () => await ISequenceMappingOperationsService.Request.OpCustomSequenceOfStringParameterAsync(
                new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
                {
                    Payload = requestPayload
                },
                default),
            Throws.Nothing);
    }

    [Test]
    public async Task Return_fixed_length_enum_custom_sequence()
    {
        // Arrange
        var value = new MyFixedLengthEnum[]
        {
            MyFixedLengthEnum.SEnum1,
            MyFixedLengthEnum.SEnum2,
            MyFixedLengthEnum.SEnum3
        };
        PipeReader responsePayload =
            ISequenceMappingOperationsService.Response.OpCustomSequenceOfMyFixedLengthEnumReturn(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        CustomSequence<MyFixedLengthEnum> r =
            await SequenceMappingOperationsProxy.Response.OpCustomSequenceOfMyFixedLengthEnumReturnAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(r, Is.EqualTo(new CustomSequence<MyFixedLengthEnum>(value)));
    }

    [Test]
    public void Custom_sequence_of_fixed_length_enum_parameter()
    {
        // Act
        PipeReader requestPayload = SequenceMappingOperationsProxy.Request.OpCustomSequenceOfMyFixedLengthEnumParameter(
            new CustomSequence<MyFixedLengthEnum>(
                new MyFixedLengthEnum[]
                {
                    MyFixedLengthEnum.SEnum1,
                    MyFixedLengthEnum.SEnum2,
                    MyFixedLengthEnum.SEnum3
                }));

        // Assert
        Assert.That(
            async () => await ISequenceMappingOperationsService.Request.OpCustomSequenceOfMyFixedLengthEnumParameterAsync(
                new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
                {
                    Payload = requestPayload
                },
                default),
            Throws.Nothing);
    }

    [Test]
    public async Task Custom_sequence_of_var_length_enum_return()
    {
        // Arrange
        var value = new MyVarLengthEnum[] { MyVarLengthEnum.Enum1, MyVarLengthEnum.Enum2, MyVarLengthEnum.Enum3 };
        PipeReader responsePayload =
            ISequenceMappingOperationsService.Response.OpCustomSequenceOfMyVarLengthEnumReturn(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        CustomSequence<MyVarLengthEnum> r =
            await SequenceMappingOperationsProxy.Response.OpCustomSequenceOfMyVarLengthEnumReturnAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(r, Is.EqualTo(new CustomSequence<MyVarLengthEnum>(value)));
    }

    [Test]
    public void Custom_sequence_of_var_length_enum_parameter()
    {
        // Act
        PipeReader requestPayload = SequenceMappingOperationsProxy.Request.OpCustomSequenceOfMyVarLengthEnumParameter(
            new CustomSequence<MyVarLengthEnum>(
                new MyVarLengthEnum[]
                {
                    MyVarLengthEnum.Enum1,
                    MyVarLengthEnum.Enum2,
                    MyVarLengthEnum.Enum3
                }));

        // Assert
        Assert.That(
            async () => await ISequenceMappingOperationsService.Request.OpCustomSequenceOfMyVarLengthEnumParameterAsync(
                new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
                {
                    Payload = requestPayload
                },
                default),
            Throws.Nothing);
    }

    [Test]
    public async Task Custom_sequence_of_unchecked_enum_return()
    {
        // Arrange
        var value = new MyUncheckedEnum[] { MyUncheckedEnum.E1, MyUncheckedEnum.E2, MyUncheckedEnum.E3 };
        PipeReader responsePayload = ISequenceMappingOperationsService.Response.OpCustomSequenceOfMyUncheckedEnumReturn(
            value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        CustomSequence<MyUncheckedEnum> r =
            await SequenceMappingOperationsProxy.Response.OpCustomSequenceOfMyUncheckedEnumReturnAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(r, Is.EqualTo(new CustomSequence<MyUncheckedEnum>(value)));
    }

    [Test]
    public void Custom_sequence_of_unchecked_enum_parameter()
    {
        // Act
        PipeReader requestPayload = SequenceMappingOperationsProxy.Request.OpCustomSequenceOfMyUncheckedEnumParameter(
            new CustomSequence<MyUncheckedEnum>(
                new MyUncheckedEnum[]
                {
                    MyUncheckedEnum.E1,
                    MyUncheckedEnum.E2,
                    MyUncheckedEnum.E3
                }));

        // Assert
        Assert.That(
            async () => await ISequenceMappingOperationsService.Request.OpCustomSequenceOfMyUncheckedEnumParameterAsync(
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
