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
    public void Decode_nested_custom_collection_fields()
    {
        // Arrange
        int[][] sequenceOfSequences = [[1, 2], [3]];
        var dictionaryOfSequences = new Dictionary<string, int[]> { ["a"] = [4, 5] };
        Dictionary<int, int>[] sequenceOfDictionaries = [new() { [6] = 7 }];
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer);
        encoder.EncodeSequence(
            sequenceOfSequences,
            (ref SliceEncoder encoder, int[] value) => encoder.EncodeSequence(value));
        encoder.EncodeDictionary(
            dictionaryOfSequences,
            (ref SliceEncoder encoder, string key) => encoder.EncodeString(key),
            (ref SliceEncoder encoder, int[] value) => encoder.EncodeSequence(value));
        encoder.EncodeSequence(
            sequenceOfDictionaries,
            (ref SliceEncoder encoder, Dictionary<int, int> value) => encoder.EncodeDictionary(
                value,
                (ref SliceEncoder encoder, int key) => encoder.EncodeInt32(key),
                (ref SliceEncoder encoder, int value) => encoder.EncodeInt32(value)));
        var decoder = new SliceDecoder(buffer.WrittenMemory);

        // Act
        var sut = new NestedCustomCollections(ref decoder);

        // Assert
        Assert.That(sut.SequenceOfCustomSequences, Is.EqualTo(sequenceOfSequences).And.All.InstanceOf<List<int>>());
        Assert.That(sut.DictionaryOfCustomSequences, Is.EqualTo(dictionaryOfSequences));
        Assert.That(sut.DictionaryOfCustomSequences.Values, Is.All.InstanceOf<List<int>>());
        Assert.That(
            sut.SequenceOfCustomDictionaries,
            Is.EqualTo(sequenceOfDictionaries).And.All.InstanceOf<CustomDictionary<int, int>>());
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }
}
