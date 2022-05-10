// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using NUnit.Framework;

namespace IceRpc.Slice.Tests;

public class CustomTypeTests
{
    [Test]
    public void Decode_custom_type(
        [Values(SliceEncoding.Slice1, SliceEncoding.Slice2)] SliceEncoding encoding)
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, encoding);
        var expected = new MyCustomType { Flag = true, Value = 10 };
        encoder.EncodeCustomType(expected);
        var decoder = new SliceDecoder(buffer.WrittenMemory, encoding);

        var value = new StructWithCustomTypeMember(ref decoder);
        Assert.That(value.M, Is.EqualTo(expected));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Encode_custom_type(
    [Values(SliceEncoding.Slice1, SliceEncoding.Slice2)] SliceEncoding encoding)
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, encoding);
        var expected = new StructWithCustomTypeMember(new MyCustomType { Flag = true, Value = 10 });

        expected.Encode(ref encoder);

        var decoder = new SliceDecoder(buffer.WrittenMemory, encoding);

        var value = decoder.DecodeCustomType();
        Assert.That(expected.M, Is.EqualTo(value));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }
}

public record struct MyCustomType
{
    public bool Flag;
    public int Value;
}

public static class SliceEncoderCustomTypeExtensions
{
    public static void EncodeCustomType(this ref SliceEncoder encoder, MyCustomType myCustom)
    {
        encoder.EncodeBool(myCustom.Flag);
        encoder.EncodeInt32(myCustom.Value);
    }
}

public static class SliceDecoderCustomTypeExtensions
{
    public static MyCustomType DecodeCustomType(this ref SliceDecoder decoder)
    {
        MyCustomType myCustom;
        myCustom.Flag = decoder.DecodeBool();
        myCustom.Value = decoder.DecodeInt32();
        return myCustom;
    }
}
