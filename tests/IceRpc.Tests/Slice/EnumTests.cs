// Copyright (c) ZeroC, Inc.

using IceRpc.Slice;
using NUnit.Framework;
using System.IO.Pipelines;

namespace IceRpc.Tests.Slice;

public class EnumTests
{
    [TestCase((int)MyEnum.Enum1, 0)]
    [TestCase((int)MyEnum.Enum2, 1)]
    [TestCase((int)MyEnum.Enum3, 2)]
    [TestCase((short)MyFixedLengthEnum.SEnum1, 0)]
    [TestCase((short)MyFixedLengthEnum.SEnum2, 1)]
    [TestCase((short)MyFixedLengthEnum.SEnum3, 2)]
    [TestCase((int)MyEnumWithCustomEnumerators.Enum1, -10)]
    [TestCase((int)MyEnumWithCustomEnumerators.Enum2, 20)]
    [TestCase((int)MyEnumWithCustomEnumerators.Enum3, 30)]
    [TestCase((uint)MyUncheckedEnum.E0, 1)]
    [TestCase((uint)MyUncheckedEnum.E4, 16)]
    public void Enumerator_has_the_expected_value(object value, object expectedValue) =>
        Assert.That(value, Is.EqualTo(expectedValue));

    [TestCase(-10, MyEnumWithCustomEnumerators.Enum1)]
    [TestCase(30, MyEnumWithCustomEnumerators.Enum3)]
    public void As_enum_for_an_enum_with_non_contiguous_enumerators(int value, MyEnumWithCustomEnumerators expected) =>
        Assert.That(
            value.AsMyEnumWithCustomEnumerators(),
            Is.EqualTo(expected));

    [TestCase(-30)]
    [TestCase(40)]
    public void As_enum_for_an_enum_with_non_contiguous_enumerators_fails_for_invalid_value(int value) =>
        Assert.That(
            () => value.AsMyEnumWithCustomEnumerators(),
            Throws.TypeOf<InvalidDataException>());

    [TestCase(0, MyEnum.Enum1)]
    [TestCase(2, MyEnum.Enum3)]
    public void As_enum_for_an_enum_with_contiguous_enumerators(int value, MyEnum expected) =>
        Assert.That(value.AsMyEnum(), Is.EqualTo(expected));

    [TestCase(-11)]
    [TestCase(3)]
    public void As_enum_for_an_enum_with_contiguous_enumerators_fails_for_invalid_values(int value) =>
       Assert.That(() => value.AsMyEnum(), Throws.TypeOf<InvalidDataException>());

    [TestCase(sizeof(MyFixedLengthEnum), sizeof(short))]
    [TestCase(sizeof(MyUncheckedEnum), sizeof(uint))]
    public void Enum_has_the_expected_size(int size, int expectedSize) =>
        Assert.That(size, Is.EqualTo(expectedSize));

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
        var payload = EnumOperationsProxy.Request.OpCheckedEnumSeq(expected);

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
        var payload = EnumOperationsProxy.Request.OpCheckedEnumWithFixedLengthSeq(expected);

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
        var payload = EnumOperationsProxy.Request.OpUncheckedEnumSeq(expected.AsMemory());

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
