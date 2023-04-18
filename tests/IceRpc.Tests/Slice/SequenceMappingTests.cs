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
    public async Task Return_numeric_sequence()
    {
        // Arrange
        var value = new int[] { 1, 2, 3 };
        PipeReader responsePayload = ISequenceMappingOperationsService.Response.OpReturnNumericSequence(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        int[] r =
            await SequenceMappingOperationsProxy.Response.OpReturnNumericSequenceAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(r, Is.EqualTo(value));
    }

    [Test]
    public void Numeric_sequence_parameter()
    {
        // Arrange
        var value = new int[] { 1, 2, 3 };

        // Act
        PipeReader requestPayload = SequenceMappingOperationsProxy.Request.OpNumericSequenceParameter(value);

        // Assert
        Assert.That(
            async () => await ISequenceMappingOperationsService.Request.OpNumericSequenceParameterAsync(
                new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
                {
                    Payload = requestPayload
                },
                default),
            Throws.Nothing);
    }

    [Test]
    public async Task Return_string_sequence()
    {
        // Arrange
        var value = new string[] { "one", "two", "three" };
        PipeReader responsePayload = ISequenceMappingOperationsService.Response.OpReturnStringSequence(
            value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        string[] r =
            await SequenceMappingOperationsProxy.Response.OpReturnStringSequenceAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(r, Is.EqualTo(value));
    }

    [Test]
    public void String_sequence_parameter()
    {
        // Act
        PipeReader requestPayload = SequenceMappingOperationsProxy.Request.OpStringSequenceParameter(
            new string[] { "one", "two", "three" });

        // Assert
        Assert.That(
            async () => await ISequenceMappingOperationsService.Request.OpStringSequenceParameterAsync(
                new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
                {
                    Payload = requestPayload
                },
                default),
            Throws.Nothing);
    }

    [Test]
    public async Task Return_fixed_length_enum_sequence()
    {
        // Arrange
        var value = new MyFixedLengthEnum[]
        {
            MyFixedLengthEnum.SEnum1,
            MyFixedLengthEnum.SEnum2,
            MyFixedLengthEnum.SEnum3
        };
        PipeReader responsePayload =
            ISequenceMappingOperationsService.Response.OpReturnMyFixedLengthEnumSequence(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        MyFixedLengthEnum[] r =
            await SequenceMappingOperationsProxy.Response.OpReturnMyFixedLengthEnumSequenceAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(r, Is.EqualTo(value));
    }

    [Test]
    public void Fixed_length_enum_sequence_parameter()
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
            SequenceMappingOperationsProxy.Request.OpMyFixedLengthEnumCustomSequenceParameter(value);

        // Assert
        Assert.That(
            async () => await ISequenceMappingOperationsService.Request.OpMyFixedLengthEnumCustomSequenceParameterAsync(
                new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
                {
                    Payload = requestPayload
                },
                default),
            Throws.Nothing);
    }

    [Test]
    public async Task Return_var_length_enum_sequence()
    {
        // Arrange
        var value = new MyVarLengthEnum[] { MyVarLengthEnum.Enum1, MyVarLengthEnum.Enum2, MyVarLengthEnum.Enum3 };
        PipeReader responsePayload =
            ISequenceMappingOperationsService.Response.OpReturnMyVarLengthEnumCustomSequence(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        MyVarLengthEnum[] r =
            await SequenceMappingOperationsProxy.Response.OpReturnMyVarLengthEnumSequenceAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(r, Is.EqualTo(value));
    }

    [Test]
    public void Var_length_enum_sequence_parameter()
    {
        // Arrange
        var value = new MyVarLengthEnum[]
        {
            MyVarLengthEnum.Enum1,
            MyVarLengthEnum.Enum2,
            MyVarLengthEnum.Enum3
        };

        // Act
        PipeReader requestPayload = SequenceMappingOperationsProxy.Request.OpMyVarLengthEnumSequenceParameter(value);

        // Assert
        Assert.That(
            async () => await ISequenceMappingOperationsService.Request.OpMyVarLengthEnumSequenceParameterAsync(
                new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
                {
                    Payload = requestPayload
                },
                default),
            Throws.Nothing);
    }

    [Test]
    public async Task Return_unchecked_enum_sequence()
    {
        // Arrange
        var value = new MyUncheckedEnum[] { MyUncheckedEnum.E1, MyUncheckedEnum.E2, MyUncheckedEnum.E3 };
        PipeReader responsePayload = ISequenceMappingOperationsService.Response.OpReturnMyUncheckedEnumSequence(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        MyUncheckedEnum[] r =
            await SequenceMappingOperationsProxy.Response.OpReturnMyUncheckedEnumSequenceAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(r, Is.EqualTo(value));
    }

    [Test]
    public void Unchecked_enum_sequence_parameter()
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
            SequenceMappingOperationsProxy.Request.OpMyUncheckedEnumCustomSequenceParameter(value);

        // Assert
        Assert.That(
            async () => await ISequenceMappingOperationsService.Request.OpMyUncheckedEnumCustomSequenceParameterAsync(
                new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
                {
                    Payload = requestPayload
                },
                default),
            Throws.Nothing);
    }

    [Test]
    public async Task Return_numeric_custom_sequence()
    {
        // Arrange
        var value = new int[] { 1, 2, 3 };
        PipeReader responsePayload = ISequenceMappingOperationsService.Response.OpReturnNumericCustomSequence(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        CustomSequence<int> r =
            await SequenceMappingOperationsProxy.Response.OpReturnNumericCustomSequenceAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(r, Is.EqualTo(new CustomSequence<int>(value)));
    }

    [Test]
    public void Numeric_custom_sequence_parameter()
    {
        // Act
        PipeReader requestPayload = SequenceMappingOperationsProxy.Request.OpNumericCustomSequenceParameter(
            new CustomSequence<int>(new int[] { 1, 2, 3 }));

        // Assert
        Assert.That(
            async () => await ISequenceMappingOperationsService.Request.OpNumericCustomSequenceParameterAsync(
                new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
                {
                    Payload = requestPayload
                },
                default),
            Throws.Nothing);
    }

    [Test]
    public async Task Return_string_custom_sequence()
    {
        // Arrange
        var value = new string[] { "one", "two", "three" };
        PipeReader responsePayload = ISequenceMappingOperationsService.Response.OpReturnStringCustomSequence(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        CustomSequence<string> r =
            await SequenceMappingOperationsProxy.Response.OpReturnStringCustomSequenceAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(r, Is.EqualTo(new CustomSequence<string>(value)));
    }

    [Test]
    public void String_custom_sequence_parameter()
    {
        // Act
        PipeReader requestPayload = SequenceMappingOperationsProxy.Request.OpStringCustomSequenceParameter(
            new CustomSequence<string>(new string[] { "one", "two", "three" }));

        // Assert
        Assert.That(
            async () => await ISequenceMappingOperationsService.Request.OpStringCustomSequenceParameterAsync(
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
            ISequenceMappingOperationsService.Response.OpReturnMyFixedLengthEnumCustomSequence(value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        CustomSequence<MyFixedLengthEnum> r =
            await SequenceMappingOperationsProxy.Response.OpReturnMyFixedLengthEnumCustomSequenceAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(r, Is.EqualTo(new CustomSequence<MyFixedLengthEnum>(value)));
    }

    [Test]
    public void Fixed_length_enum_custom_sequence_parameter()
    {
        // Act
        PipeReader requestPayload = SequenceMappingOperationsProxy.Request.OpMyFixedLengthEnumCustomSequenceParameter(
            new CustomSequence<MyFixedLengthEnum>(
                new MyFixedLengthEnum[]
                {
                    MyFixedLengthEnum.SEnum1,
                    MyFixedLengthEnum.SEnum2,
                    MyFixedLengthEnum.SEnum3
                }));

        // Assert
        Assert.That(
            async () => await ISequenceMappingOperationsService.Request.OpMyFixedLengthEnumCustomSequenceParameterAsync(
                new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
                {
                    Payload = requestPayload
                },
                default),
            Throws.Nothing);
    }

    [Test]
    public async Task Return_var_length_enum_custom_sequence()
    {
        // Arrange
        var value = new MyVarLengthEnum[] { MyVarLengthEnum.Enum1, MyVarLengthEnum.Enum2, MyVarLengthEnum.Enum3 };
        PipeReader responsePayload = ISequenceMappingOperationsService.Response.OpReturnMyVarLengthEnumCustomSequence(
            value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        CustomSequence<MyVarLengthEnum> r =
            await SequenceMappingOperationsProxy.Response.OpReturnMyVarLengthEnumCustomSequenceAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(r, Is.EqualTo(new CustomSequence<MyVarLengthEnum>(value)));
    }

    [Test]
    public void Var_length_enum_custom_sequence_parameter()
    {
        // Act
        PipeReader requestPayload = SequenceMappingOperationsProxy.Request.OpMyVarLengthEnumCustomSequenceParameter(
            new CustomSequence<MyVarLengthEnum>(
                new MyVarLengthEnum[]
                {
                    MyVarLengthEnum.Enum1,
                    MyVarLengthEnum.Enum2,
                    MyVarLengthEnum.Enum3
                }));

        // Assert
        Assert.That(
            async () => await ISequenceMappingOperationsService.Request.OpMyVarLengthEnumCustomSequenceParameterAsync(
                new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
                {
                    Payload = requestPayload
                },
                default),
            Throws.Nothing);
    }

    [Test]
    public async Task Return_unchecked_enum_custom_sequence()
    {
        // Arrange
        var value = new MyUncheckedEnum[] { MyUncheckedEnum.E1, MyUncheckedEnum.E2, MyUncheckedEnum.E3 };
        PipeReader responsePayload = ISequenceMappingOperationsService.Response.OpReturnMyUncheckedEnumCustomSequence(
            value);
        using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc));
        var response = new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = responsePayload
        };

        // Act
        CustomSequence<MyUncheckedEnum> r =
            await SequenceMappingOperationsProxy.Response.OpReturnMyUncheckedEnumCustomSequenceAsync(
                response,
                request,
                InvalidProxy.Instance,
                default);

        // Assert
        Assert.That(r, Is.EqualTo(new CustomSequence<MyUncheckedEnum>(value)));
    }

    [Test]
    public void Unchecked_enum_custom_sequence_parameter()
    {
        // Act
        PipeReader requestPayload = SequenceMappingOperationsProxy.Request.OpMyUncheckedEnumCustomSequenceParameter(
            new CustomSequence<MyUncheckedEnum>(
                new MyUncheckedEnum[]
                {
                    MyUncheckedEnum.E1,
                    MyUncheckedEnum.E2,
                    MyUncheckedEnum.E3
                }));

        // Assert
        Assert.That(
            async () => await ISequenceMappingOperationsService.Request.OpMyUncheckedEnumCustomSequenceParameterAsync(
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
