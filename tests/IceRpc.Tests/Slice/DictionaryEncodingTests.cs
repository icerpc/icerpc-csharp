// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using NUnit.Framework;

namespace IceRpc.Slice.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class DictionaryEncodingTests
{
    [Test]
    public void Encode_dictionary([Values(SliceEncoding.Slice1, SliceEncoding.Slice2)] SliceEncoding encoding)
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[1024 * 256]);
        var encoder = new SliceEncoder(buffer, encoding);
        var expected =  Enumerable.Range(0, 1024).ToDictionary(key => key, value => $"value-{value}");

        // Act
        encoder.EncodeDictionary(
            expected,
            (ref SliceEncoder encoder, int value) => encoder.EncodeInt32(value),
            (ref SliceEncoder encoder, string value) => encoder.EncodeString(value));

        // Assert
        var decoder = new SliceDecoder(buffer.WrittenMemory, encoding);
        Assert.That(decoder.DecodeSize(), Is.EqualTo(expected.Count));
        var value = new Dictionary<int, string>();
        while (decoder.Consumed != buffer.WrittenMemory.Length)
        {
            value.Add(decoder.DecodeInt32(), decoder.DecodeString());
        }
        Assert.That(value, Is.EqualTo(expected));
    }

    [Test]
    public void Encode_dictionary_with_bit_sequence()
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[1024 * 256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        var expected = Enumerable.Range(0, 1024).ToDictionary(
            key => key,
            value => value % 2 == 0 ? $"value-{value}" : null);

        // Act
        encoder.EncodeDictionaryWithBitSequence(
            expected,
            (ref SliceEncoder encoder, int value) => encoder.EncodeInt32(value),
            (ref SliceEncoder encoder, string? value) => encoder.EncodeString(value!));

        // Assert
        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);
        Assert.That(decoder.DecodeSize(), Is.EqualTo(expected.Count));
        BitSequenceReader bitSequenceReader = decoder.GetBitSequenceReader(expected.Count);
        var value = new Dictionary<int, string?>();
        while (decoder.Consumed != buffer.WrittenMemory.Length)
        {
            if (bitSequenceReader.Read())
            {
                value.Add(decoder.DecodeInt32(), decoder.DecodeString());
            }
            else
            {
                value.Add(decoder.DecodeInt32(), null);
            }
        }
        Assert.That(value, Is.EqualTo(expected));
    }
}
