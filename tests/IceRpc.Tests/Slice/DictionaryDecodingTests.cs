// Copyright (c) ZeroC, Inc.

using IceRpc.Slice;
using IceRpc.Slice.Internal;
using NUnit.Framework;
using System.Runtime.CompilerServices;

namespace IceRpc.Tests.Slice;

[Parallelizable(scope: ParallelScope.All)]
public class DictionaryDecodingTests
{
    [Test]
    public void Decode_dictionary([Values(SliceEncoding.Slice1, SliceEncoding.Slice2)] SliceEncoding encoding)
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[1024 * 256]);
        var encoder = new SliceEncoder(buffer, encoding);
        var expected = Enumerable.Range(0, 1024).ToDictionary(key => key, value => $"value-{value}");
        encoder.EncodeSize(expected.Count);
        foreach ((int key, string value) in expected)
        {
            encoder.EncodeInt32(key);
            encoder.EncodeString(value);
        }
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
    public void Decode_dictionary_with_bit_sequence()
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[1024 * 256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        var expected = Enumerable.Range(0, 1024).ToDictionary(
            key => key,
            value => value % 2 == 0 ? null : $"value-{value}");
        encoder.EncodeSize(expected.Count);
        var bitSequenceWriter = encoder.GetBitSequenceWriter(expected.Count);
        foreach ((int key, string? value) in expected)
        {
            bitSequenceWriter.Write(value is not null);
            encoder.EncodeInt32(key);
            if (value is not null)
            {
                encoder.EncodeString(value);
            }
        }
        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);

        // Act
        var decoded = decoder.DecodeDictionaryWithBitSequence(
            count => new Dictionary<int, string?>(count),
            (ref SliceDecoder decoder) => decoder.DecodeInt32(),
            (ref SliceDecoder decoder) => decoder.DecodeString());

        // Assert
        Assert.That(decoded, Is.EqualTo(expected));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Decode_dictionary_with_bit_sequence_exceeds_default_max_collection_allocation()
    {
        var buffer = new MemoryBufferWriter(new byte[1024 * 4]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        var dict = Enumerable.Range(0, 1024).ToDictionary(key => (short)key, value => (long?)null);
        encoder.EncodeDictionaryWithBitSequence(
            dict,
            (ref SliceEncoder encoder, short value) => encoder.EncodeInt16(value),
            (ref SliceEncoder encoder, long? value) => encoder.EncodeInt64(value!.Value));

        Assert.That(
            () =>
            {
                // The default max collection allocation here is a little over 8 * 1024 * 2 = 16K.
                var sut = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);
                _ = sut.DecodeDictionaryWithBitSequence(
                    count => new Dictionary<short, long?>(count),
                    (ref SliceDecoder decoder) => decoder.DecodeInt16(),
                    (ref SliceDecoder decoder) => decoder.DecodeInt64() as long?);
            },
            Throws.InstanceOf<InvalidDataException>());
    }

    [Test]
    public void Decode_dictionary_with_bit_sequence_and_custom_max_collection_allocation()
    {
        var buffer = new MemoryBufferWriter(new byte[1024 * 2 + 256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        var dict = Enumerable.Range(0, 1024).ToDictionary(key => (short)key, value => (long?)null);
        encoder.EncodeDictionaryWithBitSequence(
            dict,
            (ref SliceEncoder encoder, short value) => encoder.EncodeInt16(value),
            (ref SliceEncoder encoder, long? value) => encoder.EncodeInt64(value!.Value));

        Assert.That(
            () =>
            {
                var sut = new SliceDecoder(
                    buffer.WrittenMemory,
                    SliceEncoding.Slice2,
                    maxCollectionAllocation: 1024 * (Unsafe.SizeOf<short>() + Unsafe.SizeOf<long?>()));
                _ = sut.DecodeDictionaryWithBitSequence(
                    count => new Dictionary<short, long?>(count),
                    (ref SliceDecoder decoder) => decoder.DecodeInt16(),
                    (ref SliceDecoder decoder) => decoder.DecodeInt64() as long?);
            },
            Throws.Nothing);
    }
}
