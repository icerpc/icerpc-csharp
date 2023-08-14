// Copyright (c) ZeroC, Inc.

using NUnit.Framework;
using System.IO.Pipelines;
using ZeroC.Slice;

namespace IceRpc.Slice.Tests;

public class EnumTests
{
    [Test]
    public void Operation_with_checked_enum_sequence_parameter()
    {
        // Arrange
        var expected = new MyEnum[]
        {
            MyEnum.Enum1,
            MyEnum.Enum2,
            MyEnum.Enum3,
        };

        // Act
        var payload = EnumOperationsProxy.Request.EncodeOpCheckedEnumSeq(expected);

        // Assert
        var decoded = Decode(payload);
        Assert.That(decoded, Is.EqualTo(expected));
        payload.Complete();

        static MyEnum[] Decode(PipeReader payload)
        {
            payload.TryRead(out var readResult);
            var decoder = new SliceDecoder(readResult.Buffer, SliceEncoding.Slice2);
            decoder.SkipSize();
            return decoder.DecodeSequence((ref SliceDecoder decoder) => decoder.DecodeMyEnum());
        }
    }

    [Test]
    public void Operation_with_checked_fixed_length_enum_sequence_parameter()
    {
        // Arrange
        var expected = new MyFixedLengthEnum[]
        {
            MyFixedLengthEnum.SEnum1,
            MyFixedLengthEnum.SEnum2,
            MyFixedLengthEnum.SEnum3,
        };

        // Act
        var payload = EnumOperationsProxy.Request.EncodeOpCheckedEnumWithFixedLengthSeq(expected);

        // Assert
        var decoded = Decode(payload);
        Assert.That(decoded, Is.EqualTo(expected));
        payload.Complete();

        static MyFixedLengthEnum[] Decode(PipeReader payload)
        {
            payload.TryRead(out var readResult);
            var decoder = new SliceDecoder(readResult.Buffer, SliceEncoding.Slice2);
            decoder.SkipSize();
            return decoder.DecodeSequence((ref SliceDecoder decoder) => decoder.DecodeMyFixedLengthEnum());
        }
    }

    [Test]
    public void Operation_with_unchecked_enum_sequence_parameter()
    {
        // Arrange
        var expected = new MyUncheckedEnum[]
        {
            MyUncheckedEnum.E0,
            MyUncheckedEnum.E1,
            MyUncheckedEnum.E2,
            MyUncheckedEnum.E3
        };

        // Act
        var payload = EnumOperationsProxy.Request.EncodeOpUncheckedEnumSeq(expected.AsMemory());

        // Assert
        var decoded = Decode(payload);
        Assert.That(decoded, Is.EqualTo(expected));
        payload.Complete();

        static MyUncheckedEnum[] Decode(PipeReader payload)
        {
            payload.TryRead(out var readResult);
            var decoder = new SliceDecoder(readResult.Buffer, SliceEncoding.Slice2);
            decoder.SkipSize();
            return decoder.DecodeSequence<MyUncheckedEnum>();
        }
    }
}
