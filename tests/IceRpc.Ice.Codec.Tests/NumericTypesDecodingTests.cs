// Copyright (c) ZeroC, Inc.

using NUnit.Framework;

namespace IceRpc.Ice.Codec.Tests;

/// <summary>Test decoding built-in types.</summary>
[Parallelizable(scope: ParallelScope.All)]
public class NumericTypesDecodingTests
{
    /// <summary>Tests the decoding of boolean types.</summary>
    /// <param name="encodedBytes">An encoded byte array to decode.</param>
    /// <param name="expected">The expected value to be decoded.</param>
    [TestCase(new byte[] { 0x00 }, false)]
    [TestCase(new byte[] { 0x01 }, true)]
    public void Decode_bool_value(byte[] encodedBytes, bool expected)
    {
        var sut = new IceDecoder(encodedBytes);

        bool r1 = sut.DecodeBool();

        Assert.That(r1, Is.EqualTo(expected));
        Assert.That(sut.Consumed, Is.EqualTo(encodedBytes.Length));
    }

    [TestCase(new byte[] { })]
    [TestCase(new byte[] { 0x03 })]
    public void Decode_invalid_bool_value(byte[] encodedBytes) =>
        Assert.Throws<InvalidDataException>(() =>
        {
            var sut = new IceDecoder(encodedBytes);
            sut.DecodeBool();
        });

    /// <summary>Tests the decoding of long. Decoding any fixed size numeric is handled the same way by the
    /// IceDecoder, as such it is sufficient to just test decoding a long.</summary>
    /// <param name="encodedBytes">An encoded byte array to decode.</param>
    /// <param name="expected">The expected long to be decoded.</param>
    [TestCase(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x80 }, long.MinValue)]
    [TestCase(new byte[] { 0x00, 0xFC, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF }, -1024)]
    [TestCase(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }, 0)]
    [TestCase(new byte[] { 0x00, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }, 1024)]
    [TestCase(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x7F }, long.MaxValue)]
    public void Decode_long_value(byte[] encodedBytes, long expected)
    {
        var sut = new IceDecoder(encodedBytes);

        long r1 = sut.DecodeLong();

        Assert.That(r1, Is.EqualTo(expected));
        Assert.That(sut.Consumed, Is.EqualTo(encodedBytes.Length));
    }

    /// <summary>Tests the decoding of a byte.</summary>
    /// <param name="encodedBytes">An encoded byte array to decode.</param>
    /// <param name="expected">The expected byte to be decoded.</param>
    [TestCase(new byte[] { 42 }, 42)]
    [TestCase(new byte[] { 0xFF }, byte.MaxValue)]
    public void Decode_byte_value(byte[] encodedBytes, byte expected)
    {
        var sut = new IceDecoder(encodedBytes);

        byte r1 = sut.DecodeByte();

        Assert.That(r1, Is.EqualTo(expected));
        Assert.That(sut.Consumed, Is.EqualTo(encodedBytes.Length));
    }

    /// <summary>Tests decoding the size bytes with the 1.1 encoding.</summary>
    /// <param name="encodedBytes">The encoded byte array to decode.</param>
    /// <param name="expected">The expected size to be decoded.</param>
    [TestCase(new byte[] { 0x40 }, 64)]
    [TestCase(new byte[] { 0x9C }, 156)]
    [TestCase(new byte[] { 0xFE }, 254)]
    [TestCase(new byte[] { 0xFF, 0xFF, 0x00, 0x00, 0x00 }, 255)]
    [TestCase(new byte[] { 0xFF, 0xE8, 0x03, 0x00, 0x00 }, 1000)]
    public void Decode_size(byte[] encodedBytes, int expected)
    {
        var sut = new IceDecoder(encodedBytes);

        var r1 = sut.DecodeSize();

        Assert.That(sut.Consumed, Is.EqualTo(encodedBytes.Length));
        Assert.That(r1, Is.EqualTo(expected));
    }
}
