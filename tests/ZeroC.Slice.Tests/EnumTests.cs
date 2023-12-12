// Copyright (c) ZeroC, Inc.

using NUnit.Framework;

namespace ZeroC.Slice.Tests;

public class EnumTests
{
    [TestCase((int)MyEnum.Enum1, 0)]
    [TestCase((int)MyEnum.Enum2, 1)]
    [TestCase((int)MyEnum.Enum3, 2)]
    [TestCase((short)MyFixedSizeEnum.SEnum1, 0)]
    [TestCase((short)MyFixedSizeEnum.SEnum2, 1)]
    [TestCase((short)MyFixedSizeEnum.SEnum3, 2)]
    [TestCase((int)MyVarSizeEnum.Enum1, -10)]
    [TestCase((int)MyVarSizeEnum.Enum2, 20)]
    [TestCase((int)MyVarSizeEnum.Enum3, 30)]
    [TestCase((uint)MyUncheckedEnum.E0, 1)]
    [TestCase((uint)MyUncheckedEnum.E4, 16)]
    [TestCase((int)MySlice1Enum.Enum1, 0)]
    [TestCase((int)MySlice1Enum.Enum2, 1)]
    [TestCase((int)MySlice1Enum.Enum3, 2)]
    [TestCase((uint)MySlice1UncheckedEnum.E0, 1)]
    [TestCase((uint)MySlice1UncheckedEnum.E4, 16)]
    public void Enumerator_has_the_expected_value(object value, object expectedValue) =>
        Assert.That(value, Is.EqualTo(expectedValue));

    [TestCase(-10, MyVarSizeEnum.Enum1)]
    [TestCase(30, MyVarSizeEnum.Enum3)]
    public void As_enum_for_an_enum_with_non_contiguous_enumerators(int value, MyVarSizeEnum expected) =>
        Assert.That(
            value.AsMyVarSizeEnum(),
            Is.EqualTo(expected));

    [TestCase(-30)]
    [TestCase(40)]
    public void As_enum_for_an_enum_with_non_contiguous_enumerators_fails_for_invalid_value(int value) =>
        Assert.That(
            () => value.AsMyVarSizeEnum(),
            Throws.TypeOf<InvalidDataException>());

    [TestCase(0, MyEnum.Enum1)]
    [TestCase(2, MyEnum.Enum3)]
    public void As_enum_for_an_enum_with_contiguous_enumerators(int value, MyEnum expected) =>
        Assert.That(value.AsMyEnum(), Is.EqualTo(expected));

    [TestCase(-11)]
    [TestCase(3)]
    public void As_enum_for_an_enum_with_contiguous_enumerators_fails_for_invalid_values(int value) =>
       Assert.That(() => value.AsMyEnum(), Throws.TypeOf<InvalidDataException>());

    [TestCase(sizeof(MyFixedSizeEnum), sizeof(short))]
    [TestCase(sizeof(MyUncheckedEnum), sizeof(uint))]
    public void Enum_has_the_expected_size(int size, int expectedSize) =>
        Assert.That(size, Is.EqualTo(expectedSize));

    [Test]
    public void Cs_attribute_on_enumerator()
    {
        // Arrange / Act
        var memberInfos = typeof(MyUncheckedEnum).GetMember("E4");
        var attributes = memberInfos[0].GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false);
        var description = ((System.ComponentModel.DescriptionAttribute)attributes[0]).Description;

        // Assert
        Assert.That(description, Is.EqualTo("Sixteen"));
    }

    [Test]
    public void Encode_int32_enum([Values(MyEnum.Enum1, MyEnum.Enum2)]MyEnum expected)
    {
        var buffer = new byte[256];
        var bufferWriter = new MemoryBufferWriter(buffer);
        var encoder = new SliceEncoder(bufferWriter, SliceEncoding.Slice2);

        encoder.EncodeMyEnum(expected);

        var decoder = new SliceDecoder(buffer.AsMemory(0, encoder.EncodedByteCount), SliceEncoding.Slice2);
        var decoded = (MyEnum)decoder.DecodeInt32();

        Assert.That(decoded, Is.EqualTo(expected));
        Assert.That(decoder.Remaining, Is.EqualTo(0));
    }

    [Test]
    public void Decode_int32_enum([Values(MyEnum.Enum1, MyEnum.Enum2)] MyEnum expected)
    {
        var buffer = new byte[256];
        var bufferWriter = new MemoryBufferWriter(buffer);
        var encoder = new SliceEncoder(bufferWriter, SliceEncoding.Slice2);

        encoder.EncodeInt32((int)expected);

        var decoder = new SliceDecoder(buffer.AsMemory(0, encoder.EncodedByteCount), SliceEncoding.Slice2);
        var decoded = decoder.DecodeMyEnum();

        Assert.That(decoded, Is.EqualTo(expected));
        Assert.That(decoder.Remaining, Is.EqualTo(0));
    }

    [Test]
    public void Encode_varint32_enum([Values(MyVarSizeEnum.Enum1, MyVarSizeEnum.Enum2)] MyVarSizeEnum expected)
    {
        var buffer = new byte[256];
        var bufferWriter = new MemoryBufferWriter(buffer);
        var encoder = new SliceEncoder(bufferWriter, SliceEncoding.Slice2);

        encoder.EncodeMyVarSizeEnum(expected);

        var decoder = new SliceDecoder(buffer.AsMemory(0, encoder.EncodedByteCount), SliceEncoding.Slice2);
        var decoded = (MyVarSizeEnum)decoder.DecodeVarInt32();

        Assert.That(decoded, Is.EqualTo(expected));
        Assert.That(decoder.Remaining, Is.EqualTo(0));
    }

    [Test]
    public void Decode_varint32_enum([Values(MyVarSizeEnum.Enum1, MyVarSizeEnum.Enum2)] MyVarSizeEnum expected)
    {
        var buffer = new byte[256];
        var bufferWriter = new MemoryBufferWriter(buffer);
        var encoder = new SliceEncoder(bufferWriter, SliceEncoding.Slice2);

        encoder.EncodeVarInt32((int)expected);

        var decoder = new SliceDecoder(buffer.AsMemory(0, encoder.EncodedByteCount), SliceEncoding.Slice2);
        var decoded = decoder.DecodeMyVarSizeEnum();

        Assert.That(decoded, Is.EqualTo(expected));
        Assert.That(decoder.Remaining, Is.EqualTo(0));
    }

    [Test]
    public void Encode_unchecked_enum([Values(MyUncheckedEnum.E1, MyUncheckedEnum.E4 + 1)] MyUncheckedEnum expected)
    {
        var buffer = new byte[256];
        var bufferWriter = new MemoryBufferWriter(buffer);
        var encoder = new SliceEncoder(bufferWriter, SliceEncoding.Slice1);

        encoder.EncodeMyUncheckedEnum(expected);

        var decoder = new SliceDecoder(buffer.AsMemory(0, encoder.EncodedByteCount), SliceEncoding.Slice1);
        var decoded = (MyUncheckedEnum)decoder.DecodeUInt32();

        Assert.That(decoded, Is.EqualTo(expected));
        Assert.That(decoder.Remaining, Is.EqualTo(0));
    }

    [Test]
    public void Decode_unchecked_enum([Values(MyUncheckedEnum.E1, MyUncheckedEnum.E4 + 1)] MyUncheckedEnum expected)
    {
        var buffer = new byte[256];
        var bufferWriter = new MemoryBufferWriter(buffer);
        var encoder = new SliceEncoder(bufferWriter, SliceEncoding.Slice2);

        encoder.EncodeUInt32((uint)expected);

        var decoder = new SliceDecoder(buffer.AsMemory(0, encoder.EncodedByteCount), SliceEncoding.Slice2);
        var decoded = decoder.DecodeMyUncheckedEnum();

        Assert.That(decoded, Is.EqualTo(expected));
        Assert.That(decoder.Remaining, Is.EqualTo(0));
    }

    [Test]
    public void Encode_slice1_enum([Values(MySlice1Enum.Enum1, MySlice1Enum.Enum2)] MySlice1Enum expected)
    {
        var buffer = new byte[256];
        var bufferWriter = new MemoryBufferWriter(buffer);
        var encoder = new SliceEncoder(bufferWriter, SliceEncoding.Slice1);

        encoder.EncodeMySlice1Enum(expected);

        var decoder = new SliceDecoder(buffer.AsMemory(0, encoder.EncodedByteCount), SliceEncoding.Slice1);
        var decoded = (MySlice1Enum)decoder.DecodeSize();

        Assert.That(decoded, Is.EqualTo(expected));
        Assert.That(decoder.Remaining, Is.EqualTo(0));
    }

    [Test]
    public void Decode_slice1_enum([Values(MySlice1Enum.Enum1, MySlice1Enum.Enum2)] MySlice1Enum expected)
    {
        var buffer = new byte[256];
        var bufferWriter = new MemoryBufferWriter(buffer);
        var encoder = new SliceEncoder(bufferWriter, SliceEncoding.Slice1);

        encoder.EncodeSize((int)expected);

        var decoder = new SliceDecoder(buffer.AsMemory(0, encoder.EncodedByteCount), SliceEncoding.Slice1);
        var decoded = decoder.DecodeMySlice1Enum();

        Assert.That(decoded, Is.EqualTo(expected));
        Assert.That(decoder.Remaining, Is.EqualTo(0));
    }

    [Test]
    public void Encode_slice1_unchecked_enum(
    [Values(MySlice1UncheckedEnum.E1, MySlice1UncheckedEnum.E4 + 1)] MySlice1UncheckedEnum expected)
    {
        var buffer = new byte[256];
        var bufferWriter = new MemoryBufferWriter(buffer);
        var encoder = new SliceEncoder(bufferWriter, SliceEncoding.Slice1);

        encoder.EncodeMySlice1UncheckedEnum(expected);

        var decoder = new SliceDecoder(buffer.AsMemory(0, encoder.EncodedByteCount), SliceEncoding.Slice1);
        var decoded = (MySlice1UncheckedEnum)decoder.DecodeSize();

        Assert.That(decoded, Is.EqualTo(expected));
        Assert.That(decoder.Remaining, Is.EqualTo(0));
    }

    [Test]
    public void Decode_slice1_unchecked_enum(
        [Values(MySlice1UncheckedEnum.E1, MySlice1UncheckedEnum.E4 + 1)] MySlice1UncheckedEnum expected)
    {
        var buffer = new byte[256];
        var bufferWriter = new MemoryBufferWriter(buffer);
        var encoder = new SliceEncoder(bufferWriter, SliceEncoding.Slice1);

        encoder.EncodeSize((int)expected);

        var decoder = new SliceDecoder(buffer.AsMemory(0, encoder.EncodedByteCount), SliceEncoding.Slice1);
        var decoded = decoder.DecodeMySlice1UncheckedEnum();

        Assert.That(decoded, Is.EqualTo(expected));
        Assert.That(decoder.Remaining, Is.EqualTo(0));
    }
}
