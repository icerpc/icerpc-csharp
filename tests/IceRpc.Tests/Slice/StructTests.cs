// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using NUnit.Framework;

namespace IceRpc.Slice.Tests;

[Timeout(5000)]
[Parallelizable(ParallelScope.All)]
public sealed class StructTests
{
    [Test]
    public void Decode_compact_struct(
    [Values(SliceEncoding.Slice1, SliceEncoding.Slice2)] SliceEncoding encoding)
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
    public void Decode_trait()
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        encoder.EncodeString(MyStruct.SliceTypeId);
        encoder.EncodeInt32(10);
        encoder.EncodeInt32(20);
        encoder.EncodeVarInt32(Slice2Definitions.TagEndMarker);
        var decoder = new SliceDecoder(
            buffer.WrittenMemory,
            SliceEncoding.Slice2,
            activator: SliceDecoder.GetActivator(typeof(MyStruct).Assembly));

        MyStruct decoded = decoder.DecodeTrait<MyStruct>();

        Assert.That(decoded.I, Is.EqualTo(10));
        Assert.That(decoded.J, Is.EqualTo(20));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Decode_struct_with_optional_members(
        [Values(10, null)] int? k,
        [Values(20, null)] int? l)
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        var bitSequenceWriter = encoder.GetBitSequenceWriter(2);
        encoder.EncodeInt32(10);
        encoder.EncodeInt32(20);

        bitSequenceWriter.Write(k != null);
        if (k != null)
        {
            encoder.EncodeInt32(k.Value);
        }

        bitSequenceWriter.Write(l != null);
        if (l != null)
        {
            encoder.EncodeInt32(l.Value);
        }
        encoder.EncodeVarInt32(Slice2Definitions.TagEndMarker);

        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);

        // Act
        var decoded = new MyStructWithOptionalMembers(ref decoder);

        // Assert
        Assert.That(decoded.I, Is.EqualTo(10));
        Assert.That(decoded.J, Is.EqualTo(20));
        Assert.That(decoded.K, Is.EqualTo(k));
        Assert.That(decoded.L, Is.EqualTo(l));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Decode_struct_with_tagged_members(
        [Values(10, null)] int? k,
        [Values(20, null)] int? l)
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        encoder.EncodeInt32(10);
        encoder.EncodeInt32(20);
        if (k != null)
        {
            encoder.EncodeTagged(
                1,
                TagFormat.F4,
                size: 4,
                k.Value,
                (ref SliceEncoder encoder, int value) => encoder.EncodeInt32(value));
        }

        if (l != null)
        {
            encoder.EncodeTagged(
                255,
                TagFormat.F4,
                size: 4,
                l.Value,
                (ref SliceEncoder encoder, int value) => encoder.EncodeInt32(value));
        }
        encoder.EncodeVarInt32(Slice2Definitions.TagEndMarker);
        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);

        var decoded = new MyStructWithTaggedMembers(ref decoder);
        Assert.That(decoded.I, Is.EqualTo(10));
        Assert.That(decoded.J, Is.EqualTo(20));
        Assert.That(decoded.K, Is.EqualTo(k));
        Assert.That(decoded.L, Is.EqualTo(l));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Encode_compact_struct(
    [Values(SliceEncoding.Slice1, SliceEncoding.Slice2)] SliceEncoding encoding)
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
    public void Encode_trait()
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        var expected = new MyStruct(10, 20);

        expected.EncodeTrait(ref encoder);

        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);
        Assert.That(decoder.DecodeString(), Is.EqualTo(MyStruct.SliceTypeId));
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(10));
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(20));
        Assert.That(decoder.DecodeVarInt32(), Is.EqualTo(Slice2Definitions.TagEndMarker));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Encode_struct_with_optional_members(
        [Values(10, null)] int? k,
        [Values(20, null)] int? l)
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        var expected = new MyStructWithOptionalMembers(10, 20, k, l);

        expected.Encode(ref encoder);

        // Assert
        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);
        var bitSequenceReader = decoder.GetBitSequenceReader(2);
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(10));
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(20));

        if (k != null)
        {
            Assert.That(bitSequenceReader.Read(), Is.True);
            Assert.That(decoder.DecodeInt32(), Is.EqualTo(k));
        }
        else
        {
            Assert.That(bitSequenceReader.Read(), Is.False);
        }

        if (l != null)
        {
            Assert.That(bitSequenceReader.Read(), Is.True);
            Assert.That(decoder.DecodeInt32(), Is.EqualTo(l));
        }
        else
        {
            Assert.That(bitSequenceReader.Read(), Is.False);
        }
        Assert.That(decoder.DecodeVarInt32(), Is.EqualTo(Slice2Definitions.TagEndMarker));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Encode_struct_with_tagged_members(
        [Values(10, null)] int? k,
        [Values(20, null)] int? l)
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        var expected = new MyStructWithTaggedMembers(10, 20, k, l);

        expected.Encode(ref encoder);

        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(10));
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(20));
        if (k != null)
        {
            Assert.That(
                decoder.DecodeTagged(1, TagFormat.F4, (ref SliceDecoder decoder) => decoder.DecodeInt32()),
                Is.EqualTo(k));
        }

        if (l != null)
        {
            Assert.That(
                decoder.DecodeTagged(255, TagFormat.F4, (ref SliceDecoder decoder) => decoder.DecodeInt32()),
                Is.EqualTo(l));
        }
        Assert.That(decoder.DecodeVarInt32(), Is.EqualTo(Slice2Definitions.TagEndMarker));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }
}
