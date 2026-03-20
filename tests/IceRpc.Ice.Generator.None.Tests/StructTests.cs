// Copyright (c) ZeroC, Inc.

using IceRpc.Ice.Codec;
using NUnit.Framework;
using ZeroC.Tests.Common;

namespace IceRpc.Ice.Generator.None.Tests;

[Parallelizable(ParallelScope.All)]
public sealed class StructTests
{
    [Test]
    public void Decode_struct()
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new IceEncoder(buffer);
        encoder.EncodeInt(10);
        encoder.EncodeInt(20);
        var decoder = new IceDecoder(buffer.WrittenMemory);

        var decoded = new MyStruct(ref decoder);

        Assert.That(decoded.I, Is.EqualTo(10));
        Assert.That(decoded.J, Is.EqualTo(20));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Encode_struct()
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new IceEncoder(buffer);
        var expected = new MyStruct(10, 20);

        expected.Encode(ref encoder);

        var decoder = new IceDecoder(buffer.WrittenMemory);
        Assert.That(decoder.DecodeInt(), Is.EqualTo(expected.I));
        Assert.That(decoder.DecodeInt(), Is.EqualTo(expected.J));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Cs_attribute_on_field()
    {
        // Arrange / Act
        var memberInfos = typeof(MyStructWithFieldAttributes).GetMember("I");
        var attributes = memberInfos[0].GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false);
        var description = ((System.ComponentModel.DescriptionAttribute)attributes[0]).Description;

        // Assert
        Assert.That(description, Is.EqualTo("An integer"));
    }

    [Test]
    public void Clone_cs_readonly_struct()
    {
        var keyValuePair = new KeyValuePair(5, "foo");
        keyValuePair = keyValuePair with { Value = "bar" };

        // Assert
        Assert.That(keyValuePair.Key, Is.EqualTo(5));
        Assert.That(keyValuePair.Value, Is.EqualTo("bar"));
    }
}
