// Copyright (c) ZeroC, Inc.

using NUnit.Framework;
using ZeroC.Slice.Codec;
using ZeroC.Slice.Codec.Internal;
using ZeroC.Tests.Common;

namespace ZeroC.Slice.CodeGen.Tests;

public class CustomTypeTests
{
    [Test]
    public void Decode_custom_type([Values] SliceEncoding encoding)
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, encoding);
        var expected = new MyCustomType { Flag = true, Value = 10 };
        encoder.EncodeCustomType(expected);
        var decoder = new SliceDecoder(buffer.WrittenMemory, encoding);

        // Act
        var value = new StructWithCustomTypeField(ref decoder);

        // Assert
        Assert.That(value.M, Is.EqualTo(expected));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Encode_custom_type([Values] SliceEncoding encoding)
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, encoding);
        var expected = new StructWithCustomTypeField(new MyCustomType { Flag = true, Value = 10 });

        // Act
        expected.Encode(ref encoder);

        // Assert
        var decoder = new SliceDecoder(buffer.WrittenMemory, encoding);
        var value = decoder.DecodeCustomType();
        Assert.That(expected.M, Is.EqualTo(value));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Decode_sequence_of_custom_types([Values] SliceEncoding encoding)
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, encoding);
        var expected = new[]
        {
            new MyCustomType { Flag = true, Value = 79 },
            new MyCustomType { Flag = false, Value = 97 },
            new MyCustomType { Flag = true, Value = 7997 },
        };
        encoder.EncodeSequence(
            expected,
            CustomTypeSliceEncoderExtensions.EncodeCustomType);
        var decoder = new SliceDecoder(buffer.WrittenMemory, encoding);

        // Act
        var value = new StructWithSequenceOfCustomTypes(ref decoder);

        // Assert
        Assert.That(value.S, Is.EqualTo(expected));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Encode_sequence_of_custom_types([Values] SliceEncoding encoding)
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, encoding);
        var expected = new StructWithSequenceOfCustomTypes(new[]
        {
            new MyCustomType { Flag = true, Value = 79 },
            new MyCustomType { Flag = false, Value = 97 },
            new MyCustomType { Flag = true, Value = 7997 },
        });

        // Act
        expected.Encode(ref encoder);

        // Assert
        var decoder = new SliceDecoder(buffer.WrittenMemory, encoding);
        var value = decoder.DecodeSequence(CustomTypeSliceDecoderExtensions.DecodeCustomType);
        Assert.That(expected.S, Is.EqualTo(value));
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
