// Copyright (c) ZeroC, Inc.

using NUnit.Framework;

namespace ZeroC.Slice.Tests;

[Parallelizable(ParallelScope.All)]
public sealed class StructTests
{
    [Test]
    public void Decode_compact_struct([Values] SliceEncoding encoding)
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, encoding);
        encoder.EncodeInt32(10);
        encoder.EncodeInt32(20);
        var decoder = new SliceDecoder(buffer.WrittenMemory, encoding);

        var decoded = new MyCompactStruct(ref decoder);

        Assert.That(decoded.I, Is.EqualTo(10));
        Assert.That(decoded.J, Is.EqualTo(20));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Decode_struct()
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        encoder.EncodeInt32(10);
        encoder.EncodeInt32(20);
        encoder.EncodeVarInt32(Slice2Definitions.TagEndMarker);
        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);

        var decoded = new MyStruct(ref decoder);

        Assert.That(decoded.I, Is.EqualTo(10));
        Assert.That(decoded.J, Is.EqualTo(20));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Decode_struct_with_optional_fields(
        [Values(10, null)] int? k,
        [Values(20, null)] int? l,
        [Values(MyEnum.Enum1, null)] MyEnum? e)
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        var bitSequenceWriter = encoder.GetBitSequenceWriter(2);
        encoder.EncodeInt32(10);
        encoder.EncodeInt32(20);

        bitSequenceWriter.Write(k is not null);
        if (k is not null)
        {
            encoder.EncodeInt32(k.Value);
        }

        bitSequenceWriter.Write(l is not null);
        if (l is not null)
        {
            encoder.EncodeInt32(l.Value);
        }

        bitSequenceWriter.Write(e is not null);
        if (e is not null)
        {
            encoder.EncodeMyEnum(e.Value);
        }

        encoder.EncodeVarInt32(Slice2Definitions.TagEndMarker);

        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);

        // Act
        var decoded = new MyStructWithOptionalFields(ref decoder);

        // Assert
        Assert.That(decoded.I, Is.EqualTo(10));
        Assert.That(decoded.J, Is.EqualTo(20));
        Assert.That(decoded.K, Is.EqualTo(k));
        Assert.That(decoded.L, Is.EqualTo(l));
        Assert.That(decoded.E, Is.EqualTo(e));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Encode_compact_struct([Values] SliceEncoding encoding)
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, encoding);
        var expected = new MyCompactStruct(10, 20);

        expected.Encode(ref encoder);

        var decoder = new SliceDecoder(buffer.WrittenMemory, encoding);
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(expected.I));
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(expected.J));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Encode_struct()
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        var expected = new MyStruct(10, 20);

        expected.Encode(ref encoder);

        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(expected.I));
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(expected.J));
        Assert.That(decoder.DecodeVarInt32(), Is.EqualTo(Slice2Definitions.TagEndMarker));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Encode_struct_with_optional_fields(
        [Values(10, null)] int? k,
        [Values(20, null)] int? l,
        [Values(MyEnum.Enum2, null)] MyEnum? e)
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        var expected = new MyStructWithOptionalFields(10, 20, k, l, e);

        expected.Encode(ref encoder);

        // Assert
        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);
        var bitSequenceReader = decoder.GetBitSequenceReader(2);
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(10));
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(20));

        if (k is not null)
        {
            Assert.That(bitSequenceReader.Read(), Is.True);
            Assert.That(decoder.DecodeInt32(), Is.EqualTo(k));
        }
        else
        {
            Assert.That(bitSequenceReader.Read(), Is.False);
        }

        if (l is not null)
        {
            Assert.That(bitSequenceReader.Read(), Is.True);
            Assert.That(decoder.DecodeInt32(), Is.EqualTo(l));
        }
        else
        {
            Assert.That(bitSequenceReader.Read(), Is.False);
        }
        if (e is not null)
        {
            Assert.That(bitSequenceReader.Read(), Is.True);
            Assert.That(decoder.DecodeMyEnum(), Is.EqualTo(MyEnum.Enum2));
        }
        else
        {
            Assert.That(bitSequenceReader.Read(), Is.False);
        }

        Assert.That(decoder.DecodeVarInt32(), Is.EqualTo(Slice2Definitions.TagEndMarker));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Cs_attribute_on_field()
    {
        // Arrange / Act
        var memberInfos = typeof(MyStructWithFieldAttributes).GetMember("I");
        var attributes = memberInfos[0].GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false);
        var description = ((System.ComponentModel.DescriptionAttribute)attributes[0]).Description;

        // Assert
        Assert.That(description, Is.EqualTo("An integer"));
    }

    [Test]
    public void Cs_readonly_on_field()
    {
        // Arrange / Act
        var propertyInfo = typeof(MyStructWithFieldAttributes).GetProperty("J")!;
        var setMethodReturnParameterModifiers = propertyInfo.SetMethod!.ReturnParameter.GetRequiredCustomModifiers();

        // Assert
        Assert.That(setMethodReturnParameterModifiers.Contains(typeof(System.Runtime.CompilerServices.IsExternalInit)));
    }

    [Test]
    public void Clone_cs_readonly_struct()
    {
        var keyValuePair = new KeyValuePair(5, "foo");
        keyValuePair = keyValuePair with { Value = "bar" };

        // Assert
        Assert.That(keyValuePair.Key, Is.EqualTo(5));
        Assert.That(keyValuePair.Value, Is.EqualTo("bar"));
    }
}
