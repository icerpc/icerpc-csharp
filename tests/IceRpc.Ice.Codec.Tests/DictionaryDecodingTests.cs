// Copyright (c) ZeroC, Inc.

using NUnit.Framework;
using ZeroC.Tests.Common;

namespace IceRpc.Ice.Codec.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class DictionaryDecodingTests
{
    [Test]
    public void Decode_dictionary()
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[1024 * 256]);
        var encoder = new IceEncoder(buffer);
        var expected = Enumerable.Range(0, 1024).ToDictionary(key => key, value => $"value-{value}");

        // A dictionary is encoded like a sequence.
        encoder.EncodeSequence(expected, (ref IceEncoder encoder, KeyValuePair<int, string> pair) =>
        {
            encoder.EncodeInt(pair.Key);
            encoder.EncodeString(pair.Value);
        });
        var decoder = new IceDecoder(buffer.WrittenMemory);

        // Act
        var decoded = decoder.DecodeDictionary(
            count => new Dictionary<int, string>(count),
            (ref IceDecoder decoder) => decoder.DecodeInt(),
            (ref IceDecoder decoder) => decoder.DecodeString());

        // Assert
        Assert.That(decoded, Is.EqualTo(expected));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }
}
