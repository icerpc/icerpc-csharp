// Copyright (c) ZeroC, Inc.

using NUnit.Framework;
using ZeroC.Tests.Common;

namespace ZeroC.Slice.Codec.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class DictionaryDecodingTests
{
    [Test]
    public void Decode_dictionary()
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[1024 * 256]);
        var encoder = new SliceEncoder(buffer);
        var expected = Enumerable.Range(0, 1024).ToDictionary(key => key, value => $"value-{value}");

        // A dictionary is encoded like a sequence.
        encoder.EncodeSequence(expected, (ref SliceEncoder encoder, KeyValuePair<int, string> pair) =>
        {
            encoder.EncodeInt32(pair.Key);
            encoder.EncodeString(pair.Value);
        });
        var decoder = new SliceDecoder(buffer.WrittenMemory);

        // Act
        var decoded = decoder.DecodeDictionary(
            count => new Dictionary<int, string>(count),
            (ref SliceDecoder decoder) => decoder.DecodeInt32(),
            (ref SliceDecoder decoder) => decoder.DecodeString());

        // Assert
        Assert.That(decoded, Is.EqualTo(expected));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }
}
