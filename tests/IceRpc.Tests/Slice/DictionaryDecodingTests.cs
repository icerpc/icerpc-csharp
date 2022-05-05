// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using NUnit.Framework;

namespace IceRpc.Slice.Tests;

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
        var bitSequenceWritter = encoder.GetBitSequenceWriter(expected.Count);
        foreach ((int key, string? value) in expected)
        {
            bitSequenceWritter.Write(value != null);
            encoder.EncodeInt32(key);
            if (value != null)
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
}
