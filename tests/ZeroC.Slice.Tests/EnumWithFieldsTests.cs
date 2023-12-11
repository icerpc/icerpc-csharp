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
        var shape = decoder1.DecodeShape(); // shape is Shape.Unknown
        buffer.Clear();

        var encoder2 = new SliceEncoder(buffer, SliceEncoding.Slice2);
        encoder2.EncodeShape(shape);

        var decoder2 = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);

        // Act
        var decoded = decoder2.DecodeRevisedShape();

        // Assert
        Assert.That(decoded, Is.EqualTo(revisedShape)); // we didn't loose any information
    }
}
