// Copyright (c) ZeroC, Inc.

using NUnit.Framework;
using ZeroC.Slice.Internal;

namespace ZeroC.Slice.Tests;

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

    [Test]
    public void Decode_slice1_sequence_of_optional_custom_types()
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice1);
        var expected = new MyCustomType?[]
        {
            new MyCustomType { Flag = false, Value = 7997 },
            null,
            new MyCustomType { Flag = true, Value = 79 },
        };
        encoder.EncodeSequence(expected, CustomTypeSliceEncoderExtensions.EncodeNullableCustomType);
        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice1);

        // Act
        var value = new StructWithSequenceOfOptionalCustomTypes(ref decoder);

        // Assert
        Assert.That(value.S, Is.EqualTo(expected));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Encode_slice1_sequence_of_optional_custom_types()
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice1);
        var expected = new StructWithSequenceOfOptionalCustomTypes(new MyCustomType?[]
        {
            new MyCustomType { Flag = false, Value = 7997 },
            null,
            new MyCustomType { Flag = true, Value = 79 },
        });

        // Act
        expected.Encode(ref encoder);

        // Assert
        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice1);
        var value = decoder.DecodeSequence((ref SliceDecoder decoder) => decoder.DecodeNullableCustomType());

        Assert.That(expected.S, Is.EqualTo(value));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Decode_class_with_custom_type_fields()
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice1);

        var myCustomType = new MyCustomType { Flag = false, Value = 79 };
        var structWithCustomTypeField = new StructWithCustomTypeField(myCustomType);

        encoder.EncodeSize(1); // Instance marker
        encoder.EncodeUInt8(
            (byte)Slice1Definitions.TypeIdKind.String |
            (byte)Slice1Definitions.SliceFlags.IsLastSlice |
            (byte)Slice1Definitions.SliceFlags.HasTaggedFields);
        encoder.EncodeString(typeof(ClassWithCustomTypeFields).GetSliceTypeId()!);

        CustomTypeSliceEncoderExtensions.EncodeCustomType(ref encoder, myCustomType);
        CustomTypeSliceEncoderExtensions.EncodeNullableCustomType(ref encoder, null);
        structWithCustomTypeField.Encode(ref encoder);
        encoder.EncodeTagged(
            1,
            TagFormat.FSize,
            myCustomType, 
            (ref SliceEncoder encoder, MyCustomType value) =>
                CustomTypeSliceEncoderExtensions.EncodeNullableCustomType(ref encoder, value));

        encoder.EncodeUInt8(Slice1Definitions.TagEndMarker);

        var decoder = new SliceDecoder(
            buffer.WrittenMemory,
            SliceEncoding.Slice1,
            activator: IActivator.FromAssembly(typeof(ClassWithCustomTypeFields).Assembly));

        // Act
        var value = decoder.DecodeClass<ClassWithCustomTypeFields>();

        // Assert
        Assert.That(value.A, Is.EqualTo(myCustomType));
        Assert.That(value.B, Is.Null);
        Assert.That(value.C, Is.EqualTo(myCustomType));
        Assert.That(value.D, Is.EqualTo(structWithCustomTypeField));

        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Encode_class_with_custom_type_fields()
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice1);

        var myCustomType = new MyCustomType { Flag = false, Value = 79 };
        var structWithCustomTypeField = new StructWithCustomTypeField(myCustomType);

        // Act
        encoder.EncodeClass(new ClassWithCustomTypeFields(myCustomType, null, myCustomType, structWithCustomTypeField));

        // Assert
        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice1);

        Assert.That(decoder.DecodeSize(), Is.EqualTo(1)); // Instance marker
        Assert.That(
            decoder.DecodeUInt8(),
            Is.EqualTo(
                (byte)Slice1Definitions.TypeIdKind.String |
                (byte)Slice1Definitions.SliceFlags.IsLastSlice |
                (byte)Slice1Definitions.SliceFlags.HasTaggedFields));
        Assert.That(decoder.DecodeString(), Is.EqualTo(typeof(ClassWithCustomTypeFields).GetSliceTypeId()));

        Assert.That(CustomTypeSliceDecoderExtensions.DecodeCustomType(ref decoder), Is.EqualTo(myCustomType));
        Assert.That(CustomTypeSliceDecoderExtensions.DecodeNullableCustomType(ref decoder), Is.Null);
        Assert.That(new StructWithCustomTypeField(ref decoder), Is.EqualTo(structWithCustomTypeField));
        Assert.That(
            decoder.DecodeTagged(1, TagFormat.FSize, CustomTypeSliceDecoderExtensions.DecodeNullableCustomType, useTagEndMarker: false),
            Is.EqualTo(myCustomType));

        Assert.That(decoder.DecodeUInt8(), Is.EqualTo(Slice1Definitions.TagEndMarker));
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

    public static void EncodeNullableCustomType(this ref SliceEncoder encoder, MyCustomType? myCustom)
    {
        Assert.That(encoder.Encoding, Is.EqualTo(SliceEncoding.Slice1));

        encoder.EncodeString(myCustom is null ? "nope" : "yep!");
        if (myCustom is not null)
        {
            encoder.EncodeCustomType(myCustom.Value);
        }
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

    public static MyCustomType? DecodeNullableCustomType(this ref SliceDecoder decoder)
    {
        Assert.That(decoder.Encoding, Is.EqualTo(SliceEncoding.Slice1));

        return decoder.DecodeString() switch
        {
            "nope" => null,
            "yep!" => (MyCustomType?)decoder.DecodeCustomType(),
            _ => throw new InvalidDataException("decoded invalid custom type"),
        };
    }
}
