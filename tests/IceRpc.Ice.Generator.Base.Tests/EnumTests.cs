// Copyright (c) ZeroC, Inc.

using IceRpc.Ice.Codec;
using NUnit.Framework;
using ZeroC.Tests.Common;

namespace IceRpc.Ice.Generator.Base.Tests;

public class EnumTests
{
    [TestCase((int)MyPlainEnum.Enum1, 0)]
    [TestCase((int)MyPlainEnum.Enum2, 1)]
    [TestCase((int)MyPlainEnum.Enum3, 2)]
    [TestCase((uint)MyFlagsEnum.E0, 1)]
    [TestCase((uint)MyFlagsEnum.E4, 16)]
    public void Enumerator_has_the_expected_value(object value, object expectedValue) =>
        Assert.That(value, Is.EqualTo(expectedValue));

    [Test]
    public void Encode_enum([Values(MyPlainEnum.Enum1, MyPlainEnum.Enum2)] MyPlainEnum expected)
    {
        var buffer = new byte[256];
        var bufferWriter = new MemoryBufferWriter(buffer);
        var encoder = new IceEncoder(bufferWriter);

        encoder.EncodeMyPlainEnum(expected);

        var decoder = new IceDecoder(buffer.AsMemory(0, encoder.EncodedByteCount));
        var decoded = (MyPlainEnum)decoder.DecodeSize();

        Assert.That(decoded, Is.EqualTo(expected));
        Assert.That(decoder.Remaining, Is.EqualTo(0));
    }

    [Test]
    public void Decode_enum([Values(MyPlainEnum.Enum1, MyPlainEnum.Enum2)] MyPlainEnum expected)
    {
        var buffer = new byte[256];
        var bufferWriter = new MemoryBufferWriter(buffer);
        var encoder = new IceEncoder(bufferWriter);

        encoder.EncodeSize((int)expected);

        var decoder = new IceDecoder(buffer.AsMemory(0, encoder.EncodedByteCount));
        var decoded = decoder.DecodeMyPlainEnum();

        Assert.That(decoded, Is.EqualTo(expected));
        Assert.That(decoder.Remaining, Is.EqualTo(0));
    }
}
