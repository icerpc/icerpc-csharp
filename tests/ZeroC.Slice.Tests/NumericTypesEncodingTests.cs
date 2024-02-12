// Copyright (c) ZeroC, Inc.

using NUnit.Framework;

namespace ZeroC.Slice.Tests;

/// <summary>Test encoding of built-in types with the supported Slice encodings.</summary>
[Parallelizable(scope: ParallelScope.All)]
public class NumericTypesEncodingTests
{
    /// <summary>Tests the encoding of a long.</summary>
    /// <param name="value">The long to be encoded.</param>
    /// <param name="expected">The expected byte array produced from encoding value.</param>
    [TestCase(long.MinValue, new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x80 })]
    [TestCase(long.MaxValue, new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x7F })]
    [TestCase(0, new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 })]
    [TestCase(-1024, new byte[] { 0x00, 0xFC, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF })]
    [TestCase(1024, new byte[] { 0x00, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 })]
    public void Encode_long_value(long value, byte[] expected)
    {
        var buffer = new byte[256];
        var bufferWriter = new MemoryBufferWriter(buffer);
        var encoder = new SliceEncoder(bufferWriter, SliceEncoding.Slice2);

        encoder.EncodeInt64(value);

        Assert.That(encoder.EncodedByteCount, Is.EqualTo(sizeof(long)));
        Assert.That(buffer[0..bufferWriter.WrittenMemory.Length], Is.EqualTo(expected));
    }

    /// <summary>Tests the encoding of an sbyte.</summary>
    /// <param name="value">The sbyte to be encoded.</param>
    /// <param name="expected">The expected byte array produced from encoding value.</param>
    [TestCase(sbyte.MinValue, new byte[] { 0x80 })]
    [TestCase(sbyte.MaxValue, new byte[] { 0x7F })]
    public void Encode_int8_value(sbyte value, byte[] expected)
    {
        var buffer = new byte[256];
        var bufferWriter = new MemoryBufferWriter(buffer);
        var encoder = new SliceEncoder(bufferWriter, SliceEncoding.Slice2);

        encoder.EncodeInt8(value);

        Assert.That(encoder.EncodedByteCount, Is.EqualTo(sizeof(sbyte)));
        Assert.That(buffer[0..bufferWriter.WrittenMemory.Length], Is.EqualTo(expected));
    }

    /// <summary>Tests the encoding of a variable size long.</summary>
    /// <param name="value">The long to be encoded.</param>
    /// <param name="expected">The expected byte array produced from encoding value.</param>
    [TestCase(SliceEncoder.VarInt62MinValue, new byte[] { 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x80 })]
    [TestCase(-16384, new byte[] { 0x02, 0x00, 0xFF, 0xFF })]
    [TestCase(-256, new byte[] { 0x01, 0xFC })]
    [TestCase(0, new byte[] { 0x00 })]
    [TestCase(256, new byte[] { 0x01, 0x04 })]
    [TestCase(16384, new byte[] { 0x02, 0x00, 0x01, 0x00 })]
    [TestCase(SliceEncoder.VarInt62MaxValue, new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x7F })]
    public void Encode_varint62_value(long value, byte[] expected)
    {
        var buffer = new byte[256];
        var bufferWriter = new MemoryBufferWriter(buffer);
        var encoder = new SliceEncoder(bufferWriter, SliceEncoding.Slice2);

        encoder.EncodeVarInt62(value);

        Assert.That(encoder.EncodedByteCount, Is.EqualTo(expected.Length));
        Assert.That(buffer[0..bufferWriter.WrittenMemory.Length], Is.EqualTo(expected));
    }

    /// <summary>Tests that <see cref="SliceEncoder.EncodeVarInt62" /> will throw an ArgumentOutOfRangeException
    /// if the parameter is larger than the max value of a varint62 or smaller than the min value of a varint62.</summary>
    /// <param name="value">The varint62 to be encoded.</param>
    [TestCase(SliceEncoder.VarInt62MinValue - 1)]
    [TestCase(SliceEncoder.VarInt62MaxValue + 1)]
    public void Encode_varint62_out_of_range_value_fails(long value)
    {
        // Due to limitations on ref types, we cannot setup the arrange outside of the assertion. This is a result of
        // being unable to use ref local inside anonymous methods, lambda expressions, or query expressions.
        Assert.That(() =>
        {
            // Arrange
            var buffer = new byte[256];
            var bufferWriter = new MemoryBufferWriter(buffer);
            var encoder = new SliceEncoder(bufferWriter, SliceEncoding.Slice2);

            // Act
            encoder.EncodeVarInt62(value);
        }, Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    /// <summary>Tests the encoding of a variable size unsigned long.</summary>
    /// <param name="value">The ulong to be encoded.</param>
    /// <param name="expected">The expected byte array produced from encoding value.</param>
    [TestCase(SliceEncoder.VarUInt62MinValue, new byte[] { 0x00 })]
    [TestCase((ulong)512, new byte[] { 0x01, 0x08 })]
    [TestCase((ulong)32768, new byte[] { 0x02, 0x00, 0x02, 0x00 })]
    [TestCase(SliceEncoder.VarUInt62MaxValue, new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF })]
    public void Encode_varuint62_value(ulong value, byte[] expected)
    {
        var buffer = new byte[256];
        var bufferWriter = new MemoryBufferWriter(buffer);
        var encoder = new SliceEncoder(bufferWriter, SliceEncoding.Slice2);

        encoder.EncodeVarUInt62(value);

        Assert.That(encoder.EncodedByteCount, Is.EqualTo(expected.Length));
        Assert.That(buffer[0..bufferWriter.WrittenMemory.Length], Is.EqualTo(expected));
    }

    /// <summary>Tests the encoding of a variable size unsigned long.</summary>
    /// <param name="value">The ulong to be encoded.</param>
    /// <param name="expected">The expected byte array produced from encoding value.</param>
    [TestCase(SliceEncoder.VarUInt62MinValue, new byte[] { 0x00 })]
    [TestCase((ulong)512, new byte[] { 0x01, 0x08 })]
    [TestCase((ulong)32768, new byte[] { 0x02, 0x00, 0x02, 0x00 })]
    [TestCase(SliceEncoder.VarUInt62MaxValue, new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF })]
    public void Encode_varuint62_value_with_a_fixed_number_of_bytes(ulong value, byte[] expected)
    {
        var buffer = new byte[expected.Length];

        SliceEncoder.EncodeVarUInt62(value, buffer);

        Assert.That(buffer, Is.EqualTo(expected));
    }

    /// <summary>Tests that <see cref="SliceEncoder.EncodeVarUInt62(ulong, Span{byte})" /> will throw an
    /// <see cref="ArgumentOutOfRangeException"/> if the value cannot be encoded in the given number of bytes.</summary>
    /// <param name="value">The value to be encoded.</param>
    /// <param name="numBytes">The number of bytes used for encoding value.</param>
    [TestCase(64u, 1)]
    [TestCase(16_384u, 2)]
    [TestCase(1_073_741_824u, 4)]
    [TestCase(SliceEncoder.VarUInt62MaxValue + 1, 8)]
    public void Encode_varuint62_value_throws_out_of_range(ulong value, int numBytes)
    {
        // Due to limitations on ref types, we cannot setup the arrange outside of the assertion. This is a result of
        // being unable to use ref local inside anonymous methods, lambda expressions, or query expressions.
        Assert.That(() =>
        {
            var buffer = new byte[numBytes];

            SliceEncoder.EncodeVarUInt62(value, buffer);

        }, Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    /// <summary>Tests that <see cref="SliceEncoder.EncodeVarUInt62(ulong)" /> will throw an ArgumentOutOfRangeException
    /// if the parameter is larger than the max value of a varuint62.</summary>
    /// <param name="value">The value to be encoded.</param>
    [TestCase(SliceEncoder.VarUInt62MaxValue + 1)]
    public void Encode_varuint62_value_with_a_fixed_number_of_bytes_throws_out_of_range(ulong value)
    {
        // Due to limitations on ref types, we cannot setup the arrange outside of the assertion. This is a result of
        // being unable to use ref local inside anonymous methods, lambda expressions, or query expressions.
        Assert.That(() =>
        {
            var buffer = new byte[256];
            var bufferWriter = new MemoryBufferWriter(buffer);
            var encoder = new SliceEncoder(bufferWriter, SliceEncoding.Slice2);

            encoder.EncodeVarUInt62(value);
        }, Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    /// <summary>Tests the encoding of sizes with the Slice1 encoding.</summary>
    /// <param name="size">The size to encode.</param>
    /// <param name="expected">The expected byte array produced by encoding size.</param>
    [TestCase(64, new byte[] { 0x40 })]
    [TestCase(87, new byte[] { 0x57 })]
    [TestCase(154, new byte[] { 0x9A })]
    [TestCase(156, new byte[] { 0x9C })]
    [TestCase(254, new byte[] { 0xFE })]
    [TestCase(255, new byte[] { 0xFF, 0xFF, 0x00, 0x00, 0x00 })]
    [TestCase(1000, new byte[] { 0xFF, 0xE8, 0x03, 0x00, 0x00 })]
    public void Encode_size_with_slice_1(int size, byte[] expected)
    {
        var buffer = new byte[256];
        var bufferWriter = new MemoryBufferWriter(buffer);
        var encoder = new SliceEncoder(bufferWriter, SliceEncoding.Slice1);

        encoder.EncodeSize(size);

        Assert.That(encoder.EncodedByteCount, Is.EqualTo(expected.Length));
        Assert.That(buffer[0..bufferWriter.WrittenMemory.Length], Is.EqualTo(expected));
    }

    [Test]
    public void Encode_negative_size_fails([Values] SliceEncoding encoding)
    {
        var bufferWriter = new MemoryBufferWriter(new byte[256]);

        Assert.That(
            () =>
            {
                var encoder = new SliceEncoder(bufferWriter, encoding);
                encoder.EncodeSize(-10);
            },
            Throws.InstanceOf<ArgumentException>());
    }
}
