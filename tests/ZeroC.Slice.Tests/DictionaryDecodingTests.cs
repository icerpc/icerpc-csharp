// Copyright (c) ZeroC, Inc.

using NUnit.Framework;
using System.Runtime.CompilerServices;

namespace ZeroC.Slice.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class DictionaryDecodingTests
{
    [Test]
    public void Decode_dictionary([Values] SliceEncoding encoding)
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[1024 * 256]);
        var encoder = new SliceEncoder(buffer, encoding);
        var expected = Enumerable.Range(0, 1024).ToDictionary(key => key, value => $"value-{value}");

        // A dictionary is encoded like a sequence.
        encoder.EncodeSequence(expected, (ref SliceEncoder encoder, KeyValuePair<int, string> pair) =>
        {
            encoder.EncodeInt32(pair.Key);
            encoder.EncodeString(pair.Value);
        });
        var decoder = new SliceDecoder(buffer.WrittenMemory, encoding);

        // Act
        var decoded = decoder.DecodeDictionary(
            count => new Dictionary<int, string>(count),
            (ref SliceDecoder decoder) => decoder.DecodeInt32(),
            (ref SliceDecoder decoder) => decoder.DecodeString());

        // Assert
        Assert.That(decoded, Is.EqualTo(expected));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Decode_dictionary_with_optional_value_type()
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[1024 * 256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        var expected = Enumerable.Range(0, 1024).ToDictionary(
            key => key,
            value => value % 2 == 0 ? null : $"value-{value}");

        // A dictionary is encoded like a sequence.
        encoder.EncodeSequence(expected, (ref SliceEncoder encoder, KeyValuePair<int, string?> pair) =>
            new KeyValuePair(pair.Key, pair.Value).Encode(ref encoder));

        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);

        // Act
        var decoded = decoder.DecodeDictionaryWithOptionalValueType(
            count => new Dictionary<int, string?>(count),
            (ref SliceDecoder decoder) => decoder.DecodeInt32(),
            (ref SliceDecoder decoder) => decoder.DecodeString());

        // Assert
        Assert.That(decoded, Is.EqualTo(expected));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Decode_dictionary_with_optional_value_type_exceeds_default_max_collection_allocation()
    {
        var buffer = new MemoryBufferWriter(new byte[1024 * 4]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        var dict = Enumerable.Range(0, 1024).ToDictionary(key => (short)key, value => (LargeStruct?)null);
        // Each entry is encoded on 3 bytes.
        encoder.EncodeDictionaryWithOptionalValueType(
            dict,
            (ref SliceEncoder encoder, short value) => encoder.EncodeInt16(value),
            (ref SliceEncoder encoder, LargeStruct? value) => value!.Value.Encode(ref encoder));

        Assert.That(
            () =>
            {
                // The default max collection allocation here is a little over 8 * 1024 * 3 = 24K, compared to
                // an actual increase of 1024 * (2 + 24) = 26K (Unsafe.SizeOf<LargeStruct?>() is 24).
                var sut = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);
                _ = sut.DecodeDictionaryWithOptionalValueType(
                    count => new Dictionary<short, LargeStruct?>(count),
                    (ref SliceDecoder decoder) => decoder.DecodeInt16(),
                    (ref SliceDecoder decoder) => new LargeStruct(ref decoder) as LargeStruct?);
            },
            Throws.InstanceOf<InvalidDataException>());
    }

    [Test]
    public void Decode_dictionary_with_optional_value_type_and_custom_max_collection_allocation()
    {
        var buffer = new MemoryBufferWriter(new byte[1024 * 4]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        var dict = Enumerable.Range(0, 1024).ToDictionary(key => (short)key, value => (LargeStruct?)null);
        // Each entry is encoded on 3 bytes.
        encoder.EncodeDictionaryWithOptionalValueType(
            dict,
            (ref SliceEncoder encoder, short value) => encoder.EncodeInt16(value),
            (ref SliceEncoder encoder, LargeStruct? value) => value!.Value.Encode(ref encoder));

        Assert.That(
            () =>
            {
                var sut = new SliceDecoder(
                    buffer.WrittenMemory,
                    SliceEncoding.Slice2,
                    maxCollectionAllocation: 1024 * (Unsafe.SizeOf<short>() + Unsafe.SizeOf<LargeStruct?>()));
                _ = sut.DecodeDictionaryWithOptionalValueType(
                    count => new Dictionary<short, LargeStruct?>(count),
                    (ref SliceDecoder decoder) => decoder.DecodeInt16(),
                    (ref SliceDecoder decoder) => new LargeStruct(ref decoder) as LargeStruct?);
            },
            Throws.Nothing);
    }
}
