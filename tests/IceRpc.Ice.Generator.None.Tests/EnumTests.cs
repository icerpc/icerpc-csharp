// Copyright (c) ZeroC, Inc.

using IceRpc.Ice.Codec;
using NUnit.Framework;

using ZeroC.Tests.Common;

namespace IceRpc.Ice.Generator.None.Tests;

public class EnumTests
{
    [TestCase((int)MySlice1Enum.Enum1, 0)]
    [TestCase((int)MySlice1Enum.Enum2, 1)]
    [TestCase((int)MySlice1Enum.Enum3, 2)]
    [TestCase((uint)MySlice1FlagsEnum.E0, 1)]
    [TestCase((uint)MySlice1FlagsEnum.E4, 16)]
    public void Enumerator_has_the_expected_value(object value, object expectedValue) =>
        Assert.That(value, Is.EqualTo(expectedValue));

    [Test]
    public void Encode_slice1_enum([Values(MySlice1Enum.Enum1, MySlice1Enum.Enum2)] MySlice1Enum expected)
    {
        var buffer = new byte[256];
        var bufferWriter = new MemoryBufferWriter(buffer);
        var encoder = new IceEncoder(bufferWriter);

        encoder.EncodeMySlice1Enum(expected);

        var decoder = new IceDecoder(buffer.AsMemory(0, encoder.EncodedByteCount));
        var decoded = (MySlice1Enum)decoder.DecodeSize();

        Assert.That(decoded, Is.EqualTo(expected));
        Assert.That(decoder.Remaining, Is.EqualTo(0));
    }

    [Test]
    public void Decode_slice1_enum([Values(MySlice1Enum.Enum1, MySlice1Enum.Enum2)] MySlice1Enum expected)
    {
        var buffer = new byte[256];
        var bufferWriter = new MemoryBufferWriter(buffer);
        var encoder = new IceEncoder(bufferWriter);

        encoder.EncodeSize((int)expected);

        var decoder = new IceDecoder(buffer.AsMemory(0, encoder.EncodedByteCount));
        var decoded = decoder.DecodeMySlice1Enum();

        Assert.That(decoded, Is.EqualTo(expected));
        Assert.That(decoder.Remaining, Is.EqualTo(0));
    }
}
