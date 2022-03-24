// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using NUnit.Framework;

namespace IceRpc.Slice.Tests;

/// <summary>Test decoding built-in types with the supported Slice encodings.</summary>
[Parallelizable(scope: ParallelScope.All)]
public class DecodingBuiltInTypesTests
{

    /// <summary>Test the decoding of long. Decoding any fixed size numeric is handled the same way by the SliceDecoder,
    /// as such it is sufficient to just test decoding a long.</summary>
    /// <param name="encodedBytes">An encoded byte array to decode.</param>
    /// <param name="expected">The expected long to be decoded.</param>
    [TestCase(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x80 }, long.MinValue)]
    [TestCase(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x7F }, long.MaxValue)]
    public void Decoding_long(byte[] encodedBytes, long expected)
    {
        var sut = new SliceDecoder(encodedBytes, SliceEncoding.Slice20);

        long r1 = sut.DecodeLong();

        Assert.That(r1, Is.EqualTo(expected));
        Assert.That(sut.Consumed, Is.EqualTo(encodedBytes.Length));
    }

    /// <summary>Test the decoding of variable size long.</summary>
    /// <param name="encodedBytes">An encoded byte array to decode.</param>
    /// <param name="expected">The expected long to be decoded.</param>
    [TestCase(new byte[] { 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x80 }, SliceEncoder.VarLongMinValue)]
    [TestCase(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x7F }, SliceEncoder.VarLongMaxValue)]
    [TestCase(new byte[] { 0x00 }, 0)]
    [TestCase(new byte[] { 0x01, 0x04 }, 256)]
    public void Decoding_varlong(byte[] encodedBytes, long expected)
    {
        var sut = new SliceDecoder(encodedBytes, SliceEncoding.Slice20);

        long r1 = sut.DecodeVarLong();

        Assert.That(r1, Is.EqualTo(expected));
        Assert.That(sut.Consumed, Is.EqualTo(encodedBytes.Length));
    }

    /// <summary>Test the decoding of a variable length int.</summary>
    /// <param name="encodedBytes">An encoded byte array to decode.</param>
    /// <param name="expected">The expected int to be decoded.</param>
    [TestCase(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x01, 0x00, 0x00, 0x00 }, Int32.MaxValue)]
    [TestCase(new byte[] { 0xFE, 0xFF, 0xFF, 0x3F }, (int)Int32.MaxValue / 8)]
    [TestCase(new byte[] { 0x00 }, 0)]
    public void Decoding_varint(byte[] encodedBytes, int expected)
    {
        var sut = new SliceDecoder(encodedBytes, SliceEncoding.Slice20);

        int r1 = sut.DecodeVarInt();

        Assert.That(r1, Is.EqualTo(expected));
        Assert.That(sut.Consumed, Is.EqualTo(encodedBytes.Length));
    }

    /// <summary>Test that attempting to decode a variable length int that that is out of bound throws
    /// an <see cref="InvalidDataException"/>.</summary>
    /// <param name="p1">A long to encode into a byte array that will fail to be decoded into an int.</param>
    [TestCase((long)Int32.MaxValue + 1)]
    [TestCase((long)Int32.MinValue - 1)]
    public void Fail_to_decode_varint(long p1)
    {
        Assert.That(() =>
        {
            var buffer = new byte[256];
            var bufferWriter = new MemoryBufferWriter(buffer);
            var encoder = new SliceEncoder(bufferWriter, SliceEncoding.Slice20);
            encoder.EncodeVarLong(p1);
            var encodedBytes = buffer[0..bufferWriter.WrittenMemory.Length];
            var sut = new SliceDecoder(encodedBytes, SliceEncoding.Slice20);

            sut.DecodeVarInt();
        }, Throws.InstanceOf<InvalidDataException>());
    }

    /// <summary>Test the decoding of a variable size signed long.</summary>
    /// <param name="encodedBytes">>An encoded byte array to decode.</param>
    /// <param name="expected">The expected ulong to be decoded.</param>
    [TestCase(new byte[] { 0x00 }, SliceEncoder.VarULongMinValue)]
    [TestCase(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF }, SliceEncoder.VarULongMaxValue)]
    public void Decoding_varulong(byte[] encodedBytes, ulong expected)
    {
        var sut = new SliceDecoder(encodedBytes, SliceEncoding.Slice20);

        ulong r1 = sut.DecodeVarULong();

        Assert.That(r1, Is.EqualTo(expected));
        Assert.That(sut.Consumed, Is.EqualTo(encodedBytes.Length));
    }

    /// <summary>Test the decoding of a variable length unsigned int.</summary>
    /// <param name="encodedBytes">An encoded byte array to decode.</param>
    /// <param name="expected">The expected uint to be decoded.</param>
    [TestCase(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x03, 0x00, 0x00, 0x00 }, uint.MaxValue)]
    [TestCase(new byte[] { 0xFE, 0xFF, 0xFF, 0x7F }, (uint)uint.MaxValue / 8)]
    [TestCase(new byte[] { 0x00 }, uint.MinValue)]
    public void Decoding_varuint(byte[] encodedBytes, uint expected)
    {
        var sut = new SliceDecoder(encodedBytes, SliceEncoding.Slice20);

        uint r1 = sut.DecodeVarUInt();

        Assert.That(r1, Is.EqualTo(expected));
        Assert.That(sut.Consumed, Is.EqualTo(encodedBytes.Length));
    }

    /// <summary>Test that attempting to decode a variable length unisgned int that that is out of bound throws
    /// an <see cref="InvalidDataException"/>.</summary>
    /// <param name="p1">A long to encode into a byte array that will fail to be decoded into an uint.</param>
    [TestCase((ulong)UInt32.MaxValue + 1)]
    public void Fail_to_decode_varuint(ulong p1)
    {
        Assert.That(() =>
        {
            var buffer = new byte[256];
            var bufferWriter = new MemoryBufferWriter(buffer);
            var encoder = new SliceEncoder(bufferWriter, SliceEncoding.Slice20);
            encoder.EncodeVarULong(p1);
            var encodedBytes = buffer[0..bufferWriter.WrittenMemory.Length];
            var sut = new SliceDecoder(encodedBytes, SliceEncoding.Slice20);

            sut.DecodeVarUInt();
        }, Throws.InstanceOf<InvalidDataException>());
    }

    /// <summary>Tests decoding the size bytes.</summary>
    /// <param name="encodedBytes">The encoded byte array to decode.</param>
    /// <param name="encoding">The encoding to use to decode the byte array.</param>
    /// <param name="expected">The expected size to be decoded.</param>
    [TestCase(new byte[] { 0x57 }, "1.1", 87)]
    [TestCase(new byte[] { 0x5D, 0x01 }, "2.0", 87)]
    [TestCase(new byte[] { 0x9A }, "1.1", 154)]
    [TestCase(new byte[] { 0x69, 0x02 }, "2.0", 154)]
    [TestCase(new byte[] { 0x9C }, "1.1", 156)]
    [TestCase(new byte[] { 0x71, 0x02 }, "2.0", 156)]
    [TestCase(new byte[] { 0x40 }, "1.1", 64)]
    [TestCase(new byte[] { 0x01, 0x01 }, "2.0", 64)]
    public void Decoding_size(byte[] encodedBytes, string encoding, int expected)
    {
        var sut = new SliceDecoder(encodedBytes, SliceEncoding.FromString(encoding));

        var r1 = sut.DecodeSize();

        Assert.That(sut.Consumed, Is.EqualTo(encodedBytes.Length));
        Assert.That(r1, Is.EqualTo(expected));
    }

    /// <summary>Tests the decoding of a string. The only difference between decoding strings with the 1.1 encoding and
    /// the 2.0 encoding is how the size gets encoded. Since <see cref="Decoding_size(string, byte[], byte[])"/>
    /// tests the size encoding, this test only needs to verify how strings are decoded with 2.0. </summary>
    /// <param name="testString">The string to be decoded.</param>
    [TestCase("")]
    [TestCase("Lorem ipsum dolor sit amet, no explicari repudiare vis, an dicant legimus ponderum sit.")]
    [TestCase("국민경제의 발전을 위한 중요정책의 수립에 관하여 대통령의 자문에 응하기 위하여 국민경제자문회의를 둘 수 있다")] // Korean
    [TestCase("旅ロ京青利セムレ弱改フヨス波府かばぼ意送でぼ調掲察たス日西重ケアナ住橋ユムミク順待ふかんぼ人奨貯鏡すびそ")]  // Japanese
    [TestCase("😁😂😃😄😅😆😉😊😋😌😍😏😒😓😔😖")]
    public void Decoding_string(string testString)
    {
        var buffer = new byte[256];
        var bufferWriter = new MemoryBufferWriter(buffer);
        var encoder = new SliceEncoder(bufferWriter, SliceEncoding.Slice20);
        encoder.EncodeString(testString);
        byte[] encodedString = buffer[0..bufferWriter.WrittenMemory.Length];
        var sut = new SliceDecoder(encodedString, SliceEncoding.Slice20);

        var r1 = sut.DecodeString();

        Assert.That(r1, Is.EqualTo(testString));
        Assert.That(sut.Consumed, Is.EqualTo(encodedString.Length));
    }
}
