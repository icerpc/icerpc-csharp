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
        encoder.EncodeInt(10);
        encoder.EncodeInt(20);
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
        encoder.EncodeInt(10);
        encoder.EncodeInt(20);
        encoder.EncodeVarInt(Slice2Definitions.TagEndMarker);
        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);

        var decoded = new MyStruct(ref decoder);

        Assert.That(decoded.I, Is.EqualTo(10));
        Assert.That(decoded.J, Is.EqualTo(20));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Decode_struct_as_trait()
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        encoder.EncodeString(MyStruct.SliceTypeId);
        encoder.EncodeInt(10);
        encoder.EncodeInt(20);
        encoder.EncodeVarInt(Slice2Definitions.TagEndMarker);
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
        encoder.EncodeInt(10);
        encoder.EncodeInt(20);

        bitSequenceWriter.Write(k != null);
        if (k != null)
        {
            encoder.EncodeInt(k.Value);
        }

        bitSequenceWriter.Write(l != null);
        if (l != null)
        {
            encoder.EncodeInt(l.Value);
        }
        encoder.EncodeVarInt(Slice2Definitions.TagEndMarker);

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
        encoder.EncodeInt(10);
        encoder.EncodeInt(20);
        if (k != null)
        {
            encoder.EncodeTagged(
                1,
                TagFormat.F4,
                size: 4,
                k.Value,
                (ref SliceEncoder encoder, int value) => encoder.EncodeInt(value));
        }

        if (l != null)
        {
            encoder.EncodeTagged(
                255,
                TagFormat.F4,
                size: 4,
                l.Value,
                (ref SliceEncoder encoder, int value) => encoder.EncodeInt(value));
        }
        encoder.EncodeVarInt(Slice2Definitions.TagEndMarker);
        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);

        var decoded = new MyStructWithTaggedMembers(ref decoder);

        // Payload:
        //   int 4 bytes,
        //   int 4 bytes,
        //   tagged int 0 | tag(1) 1 byte, size 1 byte, data 4 bytes,
        //   optional int 0 | tag(255) 2 bytes, size 1 byte, data 4 bytes,,
        //   tag end marker 1 byte)
        int size = 4 + 4 + (k == null ? 0 : 6) + (l == null ? 0 : 7) + 1;
        Assert.That(buffer.WrittenMemory.Length, Is.EqualTo(size));
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
        Assert.That(buffer.WrittenMemory.Length, Is.EqualTo(8));
        Assert.That(decoder.DecodeInt(), Is.EqualTo(expected.I));
        Assert.That(decoder.DecodeInt(), Is.EqualTo(expected.J));
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
        Assert.That(buffer.WrittenMemory.Length, Is.EqualTo(9));
        Assert.That(decoder.DecodeInt(), Is.EqualTo(expected.I));
        Assert.That(decoder.DecodeInt(), Is.EqualTo(expected.J));
        Assert.That(decoder.DecodeVarInt(), Is.EqualTo(Slice2Definitions.TagEndMarker));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Encode_struct_as_trait()
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        var expected = new MyStruct(10, 20);
        expected.EncodeTrait(ref encoder);

        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);

        Assert.That(decoder.DecodeString(), Is.EqualTo(MyStruct.SliceTypeId));
        Assert.That(decoder.DecodeInt(), Is.EqualTo(10));
        Assert.That(decoder.DecodeInt(), Is.EqualTo(20));
        Assert.That(decoder.DecodeVarInt(), Is.EqualTo(Slice2Definitions.TagEndMarker));
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
        // Payload:
        //   int 4 bytes,
        //   int 4 bytes,
        //   bit-sequence 1 byte,
        //   optional int 0|4 bytes,
        //   optional int 0|4 bytes,
        //   tag end marker 1 byte)
        int size = 4 + 4 + 1 + (k == null ? 0 : 4) + (l == null ? 0 : 4) + 1;
        Assert.That(buffer.WrittenMemory.Length, Is.EqualTo(size));
        var bitSequenceReader = decoder.GetBitSequenceReader(2);
        Assert.That(decoder.DecodeInt(), Is.EqualTo(10));
        Assert.That(decoder.DecodeInt(), Is.EqualTo(20));

        if (k != null)
        {
            Assert.That(bitSequenceReader.Read(), Is.True);
            Assert.That(decoder.DecodeInt(), Is.EqualTo(k));
        }
        else
        {
            Assert.That(bitSequenceReader.Read(), Is.False);
        }

        if (l != null)
        {
            Assert.That(bitSequenceReader.Read(), Is.True);
            Assert.That(decoder.DecodeInt(), Is.EqualTo(l));
        }
        else
        {
            Assert.That(bitSequenceReader.Read(), Is.False);
        }
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
        // Payload:
        //   int 4 bytes,
        //   int 4 bytes,
        //   tagged int 0 | tag(1) 1 byte, size 1 byte, data 4 bytes,
        //   optional int 0 | tag(255) 2 bytes, size 1 byte, data 4 bytes,,
        //   tag end marker 1 byte)
        int size = 4 + 4 + (k == null ? 0 : 6) + (l == null ? 0 : 7) + 1;
        Assert.That(buffer.WrittenMemory.Length, Is.EqualTo(size));
        Assert.That(decoder.DecodeInt(), Is.EqualTo(10));
        Assert.That(decoder.DecodeInt(), Is.EqualTo(20));
        if (k != null)
        {
            Assert.That(
                decoder.DecodeTagged(1, TagFormat.F4, (ref SliceDecoder decoder) => decoder.DecodeInt()),
                Is.EqualTo(k));
        }

        if (l != null)
        {
            Assert.That(
                decoder.DecodeTagged(255, TagFormat.F4, (ref SliceDecoder decoder) => decoder.DecodeInt()),
                Is.EqualTo(l));
        }
        Assert.That(decoder.DecodeVarInt(), Is.EqualTo(Slice2Definitions.TagEndMarker));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }
}
