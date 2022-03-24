// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using NUnit.Framework;
namespace IceRpc.Slice.Tests;

/// <summary>Test encoding of built-in types with the supported Slice encodings.</summary>
[Parallelizable(scope: ParallelScope.All)]
public class EncodingBuiltInTypesTests
{
    /// <summary>Test the encoding of a long.</summary>
    /// <param name="p1">The long to be encoded.</param>
    /// <param name="expected">The expected byte array produced from encoding p1.</param>
    [TestCase(long.MinValue, new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x80 })]
    [TestCase(long.MaxValue, new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x7F })]
    public void Encoding_long(long p1, byte[] expected)
    {
        var buffer = new byte[256];
        var bufferWriter = new MemoryBufferWriter(buffer);
        var encoder = new SliceEncoder(bufferWriter, SliceEncoding.Slice20);

        encoder.EncodeLong(p1);

        Assert.That(encoder.EncodedByteCount, Is.EqualTo(sizeof(long)));
        Assert.That(bufferWriter.WrittenMemory.Length, Is.EqualTo(sizeof(long)));
        Assert.That(new ArraySegment<Byte>(buffer, 0, sizeof(long)), Is.EqualTo(expected));
    }

    /// <summary>Test the encoding of a variable size long.</summary>
    /// <param name="p1">The long to be encoded.</param>
    /// <param name="expected">The expected byte array produced from encoding p1.</param>
    [TestCase(SliceEncoder.VarLongMinValue, new byte[] { 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x80 })]
    [TestCase(SliceEncoder.VarLongMaxValue, new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x7F })]
    [TestCase(0, new byte[] { 0x00 })]
    [TestCase(256, new byte[] { 0x01, 0x04 })]
    public void Encoding_varlong(long p1, byte[] expected)
    {
        var buffer = new byte[256];
        var bufferWriter = new MemoryBufferWriter(buffer);
        var encoder = new SliceEncoder(bufferWriter, SliceEncoding.Slice20);

        encoder.EncodeVarLong(p1);

        Assert.That(encoder.EncodedByteCount, Is.EqualTo(expected.Length));
        Assert.That(bufferWriter.WrittenMemory.Length, Is.EqualTo(expected.Length));
        Assert.That(buffer[0..bufferWriter.WrittenMemory.Length], Is.EqualTo(expected));
    }

    /// <summary>Verifies that <see cref="SliceEncoder.EncodeVarLong"/> will throw an ArgumentOutOfRangeException
    /// if the parameter is larger than the max value of a varlong or smaller than the min value of a varlong.</summary>
    /// <param name="p1">The varlong to be encoded.</param>
    [TestCase(SliceEncoder.VarLongMinValue - 1)]
    [TestCase(SliceEncoder.VarLongMaxValue + 1)]
    public void Encoding_varlong_throws_out_of_range(long p1)
    {
        // Due to limitations on ref types, we cannot setup the arrange outside of the assertion. This is a result of
        // being unable to use ref local inside anonymous methods, lambda expressions, or query expressions.
        Assert.That(() =>
        {
            // Arrange
            var buffer = new byte[256];
            var bufferWriter = new MemoryBufferWriter(buffer);
            var encoder = new SliceEncoder(bufferWriter, SliceEncoding.Slice20);

            // Act
            encoder.EncodeVarLong(p1);
        }, Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    /// <summary>Test the encoding of a variable size unsigned long.</summary>
    /// <param name="p1">The ulong to be encoded.</param>
    /// <param name="expected">The expected byte array produced from encoding p1.</param>
    [TestCase(SliceEncoder.VarULongMinValue, new byte[] { 0x00 })]
    [TestCase(SliceEncoder.VarULongMaxValue, new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF })]
    public void Encoding_varulong(ulong p1, byte[] expected)
    {
        var buffer = new byte[256];
        var bufferWriter = new MemoryBufferWriter(buffer);
        var encoder = new SliceEncoder(bufferWriter, SliceEncoding.Slice20);

        encoder.EncodeVarULong(p1);

        Assert.That(encoder.EncodedByteCount, Is.EqualTo(expected.Length));
        Assert.That(bufferWriter.WrittenMemory.Length, Is.EqualTo(expected.Length));
        Assert.That(buffer[0..bufferWriter.WrittenMemory.Length], Is.EqualTo(expected));
    }

    /// <summary>Verifies that <see cref="SliceEncoder.EncodeVarULong"/> will throw an ArgumentOutOfRangeException
    /// if the parameter is larger than the max value of a varulong.</summary>
    /// <param name="p1">The value to be encoded.</param>
    [TestCase(SliceEncoder.VarULongMaxValue + 1)]
    public void Encoding_varulong_throws_out_of_range(ulong p1)
    {
        // Due to limitations on ref types, we cannot setup the arrange outside of the assertion. This is a result of
        // being unable to use ref local inside anonymous methods, lambda expressions, or query expressions.
        Assert.That(() =>
        {
            var buffer = new byte[256];
            var bufferWriter = new MemoryBufferWriter(buffer);
            var encoder = new SliceEncoder(bufferWriter, SliceEncoding.Slice20);

            encoder.EncodeVarULong(p1);
        }, Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    /// <summary>Tests encoding the size bytes.</summary>
    /// <param name="size">The expected size to be encoded.</param>
    /// <param name="encoding">The encoding to use to encode the byte array.</param>
    /// <param name="expected">The expected byte array produced by encoding size.</param>
    [TestCase(64, "1.1", new byte[] { 0x40 })]
    [TestCase(64, "2.0", new byte[] { 0x01, 0x01 })]
    [TestCase(87, "1.1", new byte[] { 0x57 })]
    [TestCase(87, "2.0", new byte[] { 0x5D, 0x01 })]
    [TestCase(154, "1.1", new byte[] { 0x9A })]
    [TestCase(154, "2.0", new byte[] { 0x69, 0x02 })]
    [TestCase(156, "1.1", new byte[] { 0x9C })]
    [TestCase(156, "2.0", new byte[] { 0x71, 0x02 })]
    public void Encoding_size(int size, string encoding, byte[] expected)
    {
        var buffer = new byte[256];
        var bufferWriter = new MemoryBufferWriter(buffer);
        var encoder = new SliceEncoder(bufferWriter, SliceEncoding.FromString(encoding));

        encoder.EncodeSize(size);

        Assert.That(encoder.EncodedByteCount, Is.EqualTo(expected.Length));
        Assert.That(bufferWriter.WrittenMemory.Length, Is.EqualTo(expected.Length));
        Assert.That(buffer[0..bufferWriter.WrittenMemory.Length], Is.EqualTo(expected));
    }

    /// <summary>Tests the encoding of a string. The only difference between encoding strings with the 1.1 encoding and
    /// the 2.0 encoding is how the size gets encoded. Since <see cref="Encoding_size(string, byte[], byte[])"/>
    /// tests the size encoding, this test only needs to verify how strings are encoded with 2.0. </summary>
    /// <param name="p1">The string to be encoded.</param>
    [TestCase("")]
    [TestCase("Lorem ipsum dolor sit amet, no explicari repudiare vis, an dicant legimus ponderum sit.")]
    [TestCase("국민경제의 발전을 위한 중요정책의 수립에 관하여 대통령의 자문에 응하기 위하여 국민경제자문회의를 둘 수 있다")] // Korean
    [TestCase("旅ロ京青利セムレ弱改フヨス波府かばぼ意送でぼ調掲察たス日西重ケアナ住橋ユムミク順待ふかんぼ人奨貯鏡すびそ")]  // Japanese
    [TestCase("😁😂😃😄😅😆😉😊😋😌😍😏😒😓😔😖")]
    public void Encoding_string(string p1)
    {
        var buffer = new byte[256];
        var bufferWriter = new MemoryBufferWriter(buffer);
        var encoder = new SliceEncoder(bufferWriter, SliceEncoding.Slice20);

        encoder.EncodeString(p1);

        var writtenBytes = buffer[0..bufferWriter.WrittenMemory.Length];
        var decoder = new SliceDecoder(writtenBytes, SliceEncoding.Slice20);
        var decodedString = decoder.DecodeString();
        Assert.That(encoder.EncodedByteCount, Is.EqualTo(bufferWriter.WrittenMemory.Length));
        Assert.That(p1, Is.EqualTo(decodedString));
    }
}
