// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using NUnit.Framework;
namespace IceRpc.Slice.Tests;

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

        encoder.EncodeLong(value);

        Assert.That(encoder.EncodedByteCount, Is.EqualTo(sizeof(long)));
        Assert.That(buffer[0..bufferWriter.WrittenMemory.Length], Is.EqualTo(expected));
    }

    /// <summary>Tests the encoding of a variable size long.</summary>
    /// <param name="value">The long to be encoded.</param>
    /// <param name="expected">The expected byte array produced from encoding value.</param>
    [TestCase(SliceEncoder.VarLongMinValue, new byte[] { 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x80 })]
    [TestCase(-16384, new byte[] { 0x02, 0x00, 0xFF, 0xFF })]
    [TestCase(-256, new byte[] { 0x01, 0xFC })]
    [TestCase(0, new byte[] { 0x00 })]
    [TestCase(256, new byte[] { 0x01, 0x04 })]
    [TestCase(16384, new byte[] { 0x02, 0x00, 0x01, 0x00 })]
    [TestCase(SliceEncoder.VarLongMaxValue, new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x7F })]
    public void Encode_varlong_value(long value, byte[] expected)
    {
        var buffer = new byte[256];
        var bufferWriter = new MemoryBufferWriter(buffer);
        var encoder = new SliceEncoder(bufferWriter, SliceEncoding.Slice2);

        encoder.EncodeVarLong(value);

        Assert.That(encoder.EncodedByteCount, Is.EqualTo(expected.Length));
        Assert.That(buffer[0..bufferWriter.WrittenMemory.Length], Is.EqualTo(expected));
    }

    /// <summary>Tests that <see cref="SliceEncoder.EncodeVarLong"/> will throw an ArgumentOutOfRangeException
    /// if the parameter is larger than the max value of a varlong or smaller than the min value of a varlong.</summary>
    /// <param name="value">The varlong to be encoded.</param>
    [TestCase(SliceEncoder.VarLongMinValue - 1)]
    [TestCase(SliceEncoder.VarLongMaxValue + 1)]
    public void Encode_varlong_out_of_range_value_fails(long value)
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
            encoder.EncodeVarLong(value);
        }, Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    /// <summary>Tests the encoding of a variable size unsigned long.</summary>
    /// <param name="value">The ulong to be encoded.</param>
    /// <param name="expected">The expected byte array produced from encoding value.</param>
    [TestCase(SliceEncoder.VarULongMinValue, new byte[] { 0x00 })]
    [TestCase((ulong)512, new byte[] { 0x01, 0x08 })]
    [TestCase((ulong)32768, new byte[] { 0x02, 0x00, 0x02, 0x00 })]
    [TestCase(SliceEncoder.VarULongMaxValue, new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF })]
    public void Encode_varulong_value(ulong value, byte[] expected)
    {
        var buffer = new byte[256];
        var bufferWriter = new MemoryBufferWriter(buffer);
        var encoder = new SliceEncoder(bufferWriter, SliceEncoding.Slice2);

        encoder.EncodeVarULong(value);

        Assert.That(encoder.EncodedByteCount, Is.EqualTo(expected.Length));
        Assert.That(buffer[0..bufferWriter.WrittenMemory.Length], Is.EqualTo(expected));
    }

    /// <summary>Tests that <see cref="SliceEncoder.EncodeVarULong"/> will throw an ArgumentOutOfRangeException
    /// if the parameter is larger than the max value of a varulong.</summary>
    /// <param name="value">The value to be encoded.</param>
    [TestCase(SliceEncoder.VarULongMaxValue + 1)]
    public void Encode_varulong_value_throws_out_of_range(ulong value)
    {
        // Due to limitations on ref types, we cannot setup the arrange outside of the assertion. This is a result of
        // being unable to use ref local inside anonymous methods, lambda expressions, or query expressions.
        Assert.That(() =>
        {
            var buffer = new byte[256];
            var bufferWriter = new MemoryBufferWriter(buffer);
            var encoder = new SliceEncoder(bufferWriter, SliceEncoding.Slice2);

            encoder.EncodeVarULong(value);
        }, Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    /// <summary>Tests the encoding of sizes with the 1.1 encoding.</summary>
    /// <param name="size">The size to encode.</param>
    /// <param name="expected">The expected byte array produced by encoding size.</param>
    [TestCase(64, new byte[] { 0x40 })]
    [TestCase(87, new byte[] { 0x57 })]
    [TestCase(154, new byte[] { 0x9A })]
    [TestCase(156, new byte[] { 0x9C })]
    [TestCase(254, new byte[] { 0xFE })]
    [TestCase(255, new byte[] { 0xFF, 0xFF, 0x00, 0x00, 0x00 })]
    [TestCase(1000, new byte[] { 0xFF, 0xE8, 0x03, 0x00, 0x00 })]
    public void Encode_size_with_1_1(int size, byte[] expected)
    {
        var buffer = new byte[256];
        var bufferWriter = new MemoryBufferWriter(buffer);
        var encoder = new SliceEncoder(bufferWriter, SliceEncoding.Slice1);

        encoder.EncodeSize(size);

        Assert.That(encoder.EncodedByteCount, Is.EqualTo(expected.Length));
        Assert.That(buffer[0..bufferWriter.WrittenMemory.Length], Is.EqualTo(expected));
    }
}
