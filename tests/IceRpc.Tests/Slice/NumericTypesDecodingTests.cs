// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using NUnit.Framework;

namespace IceRpc.Slice.Tests;

/// <summary>Test decoding built-in types with the supported Slice encodings.</summary>
[Parallelizable(scope: ParallelScope.All)]
public class NumericTypesDecodingTests
{
    /// <summary>Tests the decoding of long. Decoding any fixed size numeric is handled the same way by the
    /// SliceDecoder, as such it is sufficient to just test decoding a long.</summary>
    /// <param name="encodedBytes">An encoded byte array to decode.</param>
    /// <param name="expected">The expected long to be decoded.</param>
    [TestCase(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x80 }, long.MinValue)]
    [TestCase(new byte[] { 0x00, 0xFC, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF }, -1024)]
    [TestCase(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }, 0)]
    [TestCase(new byte[] { 0x00, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }, 1024)]
    [TestCase(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x7F }, long.MaxValue)]
    public void Decode_long_value(byte[] encodedBytes, long expected)
    {
        var sut = new SliceDecoder(encodedBytes, SliceEncoding.Slice2);

        long r1 = sut.DecodeInt64();

        Assert.That(r1, Is.EqualTo(expected));
        Assert.That(sut.Consumed, Is.EqualTo(encodedBytes.Length));
    }

    /// <summary>Tests the decoding of variable size long.</summary>
    /// <param name="encodedBytes">An encoded byte array to decode.</param>
    /// <param name="expected">The expected long to be decoded.</param>
    [TestCase(new byte[] { 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x80 }, SliceEncoder.VarLongMinValue)]
    [TestCase(new byte[] { 0x02, 0x00, 0xFF, 0xFF }, -16384)]
    [TestCase(new byte[] { 0x01, 0xFC }, -256)]
    [TestCase(new byte[] { 0x00 }, 0)]
    [TestCase(new byte[] { (5 << 2) + 0x02, 0x00, 0x00, 0x00 }, 5)]
    [TestCase(new byte[] { 0x01, 0x04 }, 256)]
    [TestCase(new byte[] { 0x02, 0x00, 0x01, 0x00 }, 16384)]
    [TestCase(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x7F }, SliceEncoder.VarLongMaxValue)]
    public void Decode_varlong_value(byte[] encodedBytes, long expected)
    {
        var sut = new SliceDecoder(encodedBytes, SliceEncoding.Slice2);

        long r1 = sut.DecodeVarInt62();

        Assert.That(r1, Is.EqualTo(expected));
        Assert.That(sut.Consumed, Is.EqualTo(encodedBytes.Length));
    }

    /// <summary>Tests that attempting to decode a variable size int that that is out of bound throws
    /// an <see cref="InvalidDataException"/>.</summary>
    /// <param name="value">An encoded long that will fail to be decoded into an int.</param>
    [TestCase(new byte[] { 0x03, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00 })]
    [TestCase(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFD, 0xFF, 0xFF, 0xFF })]
    public void Decode_varint_invalid_data_fails(byte[] encodedBytes)
    {
        Assert.That(() =>
        {
            var sut = new SliceDecoder(encodedBytes, SliceEncoding.Slice2);

            sut.DecodeVarInt32();
        }, Throws.InstanceOf<InvalidDataException>());
    }

    /// <summary>Tests the decoding of a variable size unsigned long.</summary>
    /// <param name="encodedBytes">>An encoded byte array to decode.</param>
    /// <param name="expected">The expected ulong to be decoded.</param>
    [TestCase(new byte[] { 0x00 }, SliceEncoder.VarULongMinValue)]
    [TestCase(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF }, SliceEncoder.VarULongMaxValue)]
    [TestCase(new byte[] { 0x16, 0x00, 0x00, 0x00 }, (ulong)5)]
    [TestCase(new byte[] { 0x01, 0x04 }, (ulong)256)]
    [TestCase(new byte[] { 0x02, 0x00, 0x01, 0x00 }, (ulong)16384)]
    public void Decode_varulong_value(byte[] encodedBytes, ulong expected)
    {
        var sut = new SliceDecoder(encodedBytes, SliceEncoding.Slice2);

        ulong r1 = sut.DecodeVarUInt62();

        Assert.That(r1, Is.EqualTo(expected));
        Assert.That(sut.Consumed, Is.EqualTo(encodedBytes.Length));
    }

    /// <summary>Tests that attempting to decode a variable length unisgned int that that is out of bound throws
    /// an <see cref="InvalidDataException"/>.</summary>
    /// <param name="value">A long to encode into a byte array that will fail to be decoded into an uint.</param>
    [TestCase((ulong)UInt32.MaxValue + 1)]
    public void Decode_varuint_invalid_data_fails(ulong value)
    {
        Assert.That(() =>
        {
            var buffer = new byte[256];
            var bufferWriter = new MemoryBufferWriter(buffer);
            var encoder = new SliceEncoder(bufferWriter, SliceEncoding.Slice2);
            encoder.EncodeVarUInt62(value);
            var encodedBytes = buffer[0..bufferWriter.WrittenMemory.Length];
            var sut = new SliceDecoder(encodedBytes, SliceEncoding.Slice2);

            sut.DecodeVarUInt32();
        }, Throws.InstanceOf<InvalidDataException>());
    }

    /// <summary>Tests decoding the size bytes with the 1.1 encoding.</summary>
    /// <param name="encodedBytes">The encoded byte array to decode.</param>
    /// <param name="encoding">The encoding to use to decode the byte array.</param>
    /// <param name="expected">The expected size to be decoded.</param>
    [TestCase(new byte[] { 0x40 }, 64)]
    [TestCase(new byte[] { 0x9C }, 156)]
    [TestCase(new byte[] { 0xFE }, 254)]
    [TestCase(new byte[] { 0xFF, 0xFF, 0x00, 0x00, 0x00 }, 255)]
    [TestCase(new byte[] { 0xFF, 0xE8, 0x03, 0x00, 0x00 }, 1000)]
    public void Decode_size(byte[] encodedBytes, int expected)
    {
        var sut = new SliceDecoder(encodedBytes, SliceEncoding.Slice1);

        var r1 = sut.DecodeSize();

        Assert.That(sut.Consumed, Is.EqualTo(encodedBytes.Length));
        Assert.That(r1, Is.EqualTo(expected));
    }
}
