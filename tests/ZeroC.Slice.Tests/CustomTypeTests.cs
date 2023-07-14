// Copyright (c) ZeroC, Inc.

using NUnit.Framework;

namespace ZeroC.Slice.Tests;

public class CustomTypeTests
{
    [Test]
    public void Decode_custom_type()
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        var expected = new MyCustomType { Flag = true, Value = 10 };
        encoder.EncodeCustomType(expected);
        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);

        var value = new StructWithCustomTypeField(ref decoder);

        Assert.That(value.M, Is.EqualTo(expected));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Encode_custom_type()
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        var expected = new StructWithCustomTypeField(new MyCustomType { Flag = true, Value = 10 });

        expected.Encode(ref encoder);

        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);
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

public static class CustomTypeSliceEncoderExtensions
{
    public static void EncodeCustomType(this ref SliceEncoder encoder, MyCustomType myCustom)
    {
        encoder.EncodeBool(myCustom.Flag);
        encoder.EncodeInt32(myCustom.Value);
    }
}

public static class CustomTypeSliceDecoderExtensions
{
    public static MyCustomType DecodeCustomType(this ref SliceDecoder decoder)
    {
        MyCustomType myCustom;
        myCustom.Flag = decoder.DecodeBool();
        myCustom.Value = decoder.DecodeInt32();
        return myCustom;
    }
}
