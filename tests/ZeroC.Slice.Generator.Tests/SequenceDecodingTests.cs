// Copyright (c) ZeroC, Inc.

using NUnit.Framework;
using System.Collections.Generic;
using System.IO;
using ZeroC.Slice.Codec;
using ZeroC.Tests.Common;

namespace ZeroC.Slice.Generator.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class SequenceDecodingTests
{
    [Test]
    public void Decode_bool_sequence_field()
    {
        // Arrange
        bool[] expected = [false, true, false];
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer);
        encoder.EncodeSize(3);
        encoder.WriteByteSpan(new byte[] { 0x00, 0x01, 0x00 });
        var decoder = new SliceDecoder(buffer.WrittenMemory);

        // Act
        var sut = new BoolS(ref decoder);

        // Assert
        Assert.That(sut.Value, Is.EqualTo(expected));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Decode_bool_sequence_field_with_invalid_values()
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer);
        encoder.EncodeSize(3);
        encoder.WriteByteSpan(new byte[] { 0x00, 0x01, 0x02 });

        // Act/Assert
        Assert.Throws<InvalidDataException>(
            () =>
            {
                var decoder = new SliceDecoder(buffer.WrittenMemory);
                _ = new BoolS(ref decoder);
            });
    }

    [Test]
    public void Decode_custom_bool_sequence_field()
    {
        // Arrange
        bool[] expected = [false, true, false];
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer);
        encoder.EncodeSize(3);
        encoder.WriteByteSpan(new byte[] { 0x00, 0x01, 0x00 });
        var decoder = new SliceDecoder(buffer.WrittenMemory);

        // Act
        var sut = new CustomBoolS(ref decoder);

        // Assert
        Assert.That(sut.Value, Is.EqualTo(expected));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Decode_custom_bool_sequence_field_with_invalid_values()
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer);
        encoder.EncodeSize(3);
        encoder.WriteByteSpan(new byte[] { 0x00, 0x01, 0x02 });

        // Act/Assert
        Assert.Throws<InvalidDataException>(
            () =>
            {
                var decoder = new SliceDecoder(buffer.WrittenMemory);
                _ = new CustomBoolS(ref decoder);
            });
    }

    [Test]
    public void Decode_custom_opt_int32_sequence_field()
    {
        // Arrange
        int?[] expected = [1, null, 3];
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer);
        encoder.EncodeSequenceOfOptionals(expected, (ref encoder, value) => encoder.EncodeInt32(value!.Value));

        var decoder = new SliceDecoder(buffer.WrittenMemory);

        // Act
        var sut = new CustomOptInt32S(ref decoder);

        // Assert
        Assert.That(sut.Value, Is.EqualTo(expected));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Decode_nested_custom_sequence_fields()
    {
        // Arrange
        int[][] nested = [[1, 2], [3]];
        var inDictionary = new Dictionary<string, int[]> { ["a"] = [4, 5] };
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer);
        encoder.EncodeSequence(nested, (ref SliceEncoder encoder, int[] value) => encoder.EncodeSequence(value));
        encoder.EncodeDictionary(
            inDictionary,
            (ref SliceEncoder encoder, string key) => encoder.EncodeString(key),
            (ref SliceEncoder encoder, int[] value) => encoder.EncodeSequence(value));
        var decoder = new SliceDecoder(buffer.WrittenMemory);

        // Act
        var sut = new NestedCustomInt32S(ref decoder);

        // Assert
        Assert.That(sut.Nested, Is.EqualTo(nested).And.All.InstanceOf<List<int>>());
        Assert.That(sut.InDictionary, Is.EqualTo(inDictionary));
        Assert.That(sut.InDictionary.Values, Is.All.InstanceOf<List<int>>());
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }
}
