// Copyright (c) ZeroC, Inc.

using NUnit.Framework;
using ZeroC.Tests.Common;

namespace IceRpc.Ice.Codec.Tests;

/// <summary>Test encoding of built-in types.</summary>
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
        var encoder = new IceEncoder(bufferWriter);

        encoder.EncodeLong(value);

        Assert.That(encoder.EncodedByteCount, Is.EqualTo(sizeof(long)));
        Assert.That(buffer[0..bufferWriter.WrittenMemory.Length], Is.EqualTo(expected));
    }

    /// <summary>Tests the encoding of an byte.</summary>
    /// <param name="value">The byte to be encoded.</param>
    /// <param name="expected">The expected byte array produced from encoding value.</param>
    [TestCase(42, new byte[] { 42 })]
    [TestCase(byte.MaxValue, new byte[] { 0xFF })]
    public void Encode_byte_value(byte value, byte[] expected)
    {
        var buffer = new byte[256];
        var bufferWriter = new MemoryBufferWriter(buffer);
        var encoder = new IceEncoder(bufferWriter);

        encoder.EncodeByte(value);

        Assert.That(encoder.EncodedByteCount, Is.EqualTo(sizeof(byte)));
        Assert.That(buffer[0..bufferWriter.WrittenMemory.Length], Is.EqualTo(expected));
    }

    /// <summary>Tests the encoding of sizes.</summary>
    /// <param name="size">The size to encode.</param>
    /// <param name="expected">The expected byte array produced by encoding size.</param>
    [TestCase(64, new byte[] { 0x40 })]
    [TestCase(87, new byte[] { 0x57 })]
    [TestCase(154, new byte[] { 0x9A })]
    [TestCase(156, new byte[] { 0x9C })]
    [TestCase(254, new byte[] { 0xFE })]
    [TestCase(255, new byte[] { 0xFF, 0xFF, 0x00, 0x00, 0x00 })]
    [TestCase(1000, new byte[] { 0xFF, 0xE8, 0x03, 0x00, 0x00 })]
    public void Encode_size(int size, byte[] expected)
    {
        var buffer = new byte[256];
        var bufferWriter = new MemoryBufferWriter(buffer);
        var encoder = new IceEncoder(bufferWriter);

        encoder.EncodeSize(size);

        Assert.That(encoder.EncodedByteCount, Is.EqualTo(expected.Length));
        Assert.That(buffer[0..bufferWriter.WrittenMemory.Length], Is.EqualTo(expected));
    }

    [Test]
    public void Encode_negative_size_fails()
    {
        var bufferWriter = new MemoryBufferWriter(new byte[256]);

        Assert.That(
            () =>
            {
                var encoder = new IceEncoder(bufferWriter);
                encoder.EncodeSize(-10);
            },
            Throws.InstanceOf<ArgumentException>());
    }
}
