// Copyright (c) ZeroC, Inc.

using NUnit.Framework;

namespace ZeroC.Slice.Tests;

public class EnumWithFieldsTests
{
    [Test]
    public void Decode_enum_ignores_unknown_tagged_fields()
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        var paintColor = new PaintColor.Blue(10);
        encoder.EncodePaintColor(paintColor);

        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);

        // Act
        var decoded = decoder.DecodeColor();

        // Assert
        Assert.That(decoded, Is.InstanceOf(typeof(Color.Blue)));
        Assert.That(decoder.Consumed, Is.EqualTo(encoder.EncodedByteCount));
    }

    [TestCase("canary", (ushort)7)]
    [TestCase("lemon", null)]
    public void Decode_enum_with_optional_field(string shade, ushort? code)
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        var paintColor = new PaintColor.Yellow(shade, code);
        encoder.EncodePaintColor(paintColor);

        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);

        // Act
        var decoded = decoder.DecodePaintColor();

        // Assert
        Assert.That(decoded, Is.EqualTo(paintColor));
        Assert.That(decoder.Consumed, Is.EqualTo(encoder.EncodedByteCount));
    }

    [Test]
    public void Decode_unchecked_enum_returns_unknown()
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        var shape = new RevisedShape.Square(10, new Color.Blue());
        encoder.EncodeRevisedShape(shape);

        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);

        // Act
        var decoded = decoder.DecodeShape();

        // Assert
        Assert.That(decoded, Is.InstanceOf(typeof(Shape.Unknown)));
        Assert.That(decoder.Consumed, Is.EqualTo(encoder.EncodedByteCount));
        Assert.That(decoded.Discriminant, Is.EqualTo(RevisedShape.Square.Discriminant));
    }

    [Test]
    public void Decode_unchecked_enum_preserves_fields()
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder1 = new SliceEncoder(buffer, SliceEncoding.Slice2);
        var revisedShape = new RevisedShape.Square(10, new Color.White());
        encoder1.EncodeRevisedShape(revisedShape);

        var decoder1 = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);
        var shape = decoder1.DecodeShape();
        buffer.Clear();

        var encoder2 = new SliceEncoder(buffer, SliceEncoding.Slice2);
        encoder2.EncodeShape(shape);

        var decoder2 = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);

        // Act
        var decoded = decoder2.DecodeRevisedShape();

        // Assert
        Assert.That(decoded, Is.EqualTo(revisedShape)); // we didn't loose any information
        Assert.That(decoder2.Consumed, Is.EqualTo(encoder2.EncodedByteCount));
        Assert.That(shape, Is.InstanceOf(typeof(Shape.Unknown)));
    }

    [TestCase("foo", 8u, 4u)]
    [TestCase(null, 7u, 3u)]
    public void Decode_enum_with_optional_field(string? name, uint major, uint minor)
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        var shape = new RevisedShape.Oval(name, major, minor);
        encoder.EncodeRevisedShape(shape);

        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);

        // Act
        var decoded = decoder.DecodeRevisedShape();

        // Assert
        Assert.That(decoded, Is.EqualTo(shape));
        Assert.That(decoder.Consumed, Is.EqualTo(encoder.EncodedByteCount));
    }
}
