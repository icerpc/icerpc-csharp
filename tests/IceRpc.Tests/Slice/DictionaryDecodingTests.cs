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
    public void Decode_dictionary_with_optional_value_type()
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[1024 * 256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        var expected = Enumerable.Range(0, 1024).ToDictionary(
            key => key,
            value => value % 2 == 0 ? null : $"value-{value}");
        encoder.EncodeSize(expected.Count);
        foreach ((int key, string? value) in expected)
        {
            encoder.EncodeBool(value is not null);
            encoder.EncodeInt32(key);
            if (value is not null)
            {
                encoder.EncodeString(value);
            }
        }
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
    public void Decode_dictionary_with_null_values()
    {
        var buffer = new MemoryBufferWriter(new byte[1024 * (1 + 2 + 8)]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        var dict = Enumerable.Range(0, 1024).ToDictionary(key => (short)key, value => (long?)null); // all null values
        encoder.EncodeDictionaryWithOptionalValueType(
            dict,
            (ref SliceEncoder encoder, short value) => encoder.EncodeInt16(value),
            (ref SliceEncoder encoder, long? value) => encoder.EncodeInt64(value!.Value));

        Assert.That(
            () =>
            {
                var sut = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);
                _ = sut.DecodeDictionaryWithOptionalValueType(
                    count => new Dictionary<short, long?>(count),
                    (ref SliceDecoder decoder) => decoder.DecodeInt16(),
                    (ref SliceDecoder decoder) => decoder.DecodeInt64() as long?);
            },
            Throws.Nothing);
    }
}
