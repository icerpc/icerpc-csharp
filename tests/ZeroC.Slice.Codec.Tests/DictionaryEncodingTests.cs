// Copyright (c) ZeroC, Inc.

using NUnit.Framework;
using ZeroC.Tests.Common;

namespace ZeroC.Slice.Codec.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class DictionaryEncodingTests
{
    [Test]
    public void Encode_dictionary()
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[1024 * 256]);
        var encoder = new SliceEncoder(buffer);
        var expected = Enumerable.Range(0, 1024).ToDictionary(key => key, value => $"value-{value}");

        // Act
        encoder.EncodeDictionary(
            expected,
            (ref SliceEncoder encoder, int value) => encoder.EncodeInt32(value),
            (ref SliceEncoder encoder, string value) => encoder.EncodeString(value));

        // Assert
        var decoder = new SliceDecoder(buffer.WrittenMemory);
        Assert.That(decoder.DecodeSize(), Is.EqualTo(expected.Count));
        var value = new Dictionary<int, string>();
        while (decoder.Consumed != buffer.WrittenMemory.Length)
        {
            value.Add(decoder.DecodeInt32(), decoder.DecodeString());
        }
        Assert.That(value, Is.EqualTo(expected));
    }
}
