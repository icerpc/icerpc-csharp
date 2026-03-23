// Copyright (c) ZeroC, Inc.

using NUnit.Framework;
using ZeroC.Tests.Common;

namespace IceRpc.Ice.Codec.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class SequenceDecodingTests
{
    /// <summary>Tests <see cref="IceDecoderExtensions.DecodeSequence{T}(ref IceDecoder, DecodeFunc{T})" /> with a
    /// fixed-size numeric value type.</summary>
    [Test]
    public void Decode_fixed_sized_numeric_sequence()
    {
        // Arrange
        int[] expected = Enumerable.Range(0, 256).Select(i => i).ToArray();
        var buffer = new MemoryBufferWriter(new byte[1024 * 1024]);
        var encoder = new IceEncoder(buffer);
        encoder.EncodeSize(expected.Length);
        foreach (int value in expected)
        {
            encoder.EncodeInt(value);
        }
        var sut = new IceDecoder(buffer.WrittenMemory);

        // Act
        int[] result = sut.DecodeSequence((ref IceDecoder decoder) => decoder.DecodeInt());

        // Assert
        Assert.That(result, Is.EqualTo(expected));
        Assert.That(sut.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    /// <summary>Tests <see cref="IceDecoderExtensions.DecodeSequence{T}(ref IceDecoder, DecodeFunc{T})" /> with a
    /// string sequence.</summary>
    [Test]
    public void Decode_string_sequence()
    {
        // Arrange
        string[] expected = Enumerable.Range(0, 256).Select(i => $"string-{i}").ToArray();
        var buffer = new MemoryBufferWriter(new byte[1024 * 1024]);
        var encoder = new IceEncoder(buffer);
        encoder.EncodeSize(expected.Length);
        foreach (string value in expected)
        {
            encoder.EncodeString(value);
        }
        var sut = new IceDecoder(buffer.WrittenMemory);

        // Act
        string[] decoded = sut.DecodeSequence((ref IceDecoder decoder) => decoder.DecodeString());

        // Assert
        Assert.That(decoded, Is.EqualTo(expected));
        Assert.That(sut.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Decode_sequence_with_element_action()
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[1024 * 1024]);
        var encoder = new IceEncoder(buffer);
        var expected = new bool[] { true, false, true, false, false, true, true, false };

        // Encode the enumerators to a buffer
        encoder.EncodeSequence(
            expected,
            (ref IceEncoder encoder, bool value) => encoder.EncodeBool(value));

        var checkedValues = new List<bool>();
        var sut = new IceDecoder(buffer.WrittenMemory);

        // Act
        bool[]? decoded = sut.DecodeSequence<bool>(value => checkedValues.Add(value));

        // Assert
        Assert.That(decoded, Is.EqualTo(expected));
        Assert.That(checkedValues, Is.EqualTo(expected));
    }
}
