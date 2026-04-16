// Copyright (c) ZeroC, Inc.

using NUnit.Framework;
using System.Runtime.CompilerServices;
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

        // A dictionary is encoded like a sequence of key-value pairs.
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

    [TestCase(10)]
    [TestCase(50)]
    [TestCase(100)]
    public void Decode_dictionary_exceeds_max_collection_allocation(int count)
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[count * (Unsafe.SizeOf<int>() + Unsafe.SizeOf<long>()) + 256]);
        var encoder = new IceEncoder(buffer);
        var dict = Enumerable.Range(0, count).ToDictionary(k => k, v => (long)v);
        encoder.EncodeDictionary(
            dict,
            (ref IceEncoder encoder, int key) => encoder.EncodeInt(key),
            (ref IceEncoder encoder, long value) => encoder.EncodeLong(value));

        int allocationLimit = (count - 1) * (Unsafe.SizeOf<int>() + Unsafe.SizeOf<long>());

        // Act/Assert
        Assert.That(
            () =>
            {
                var sut = new IceDecoder(buffer.WrittenMemory, maxCollectionAllocation: allocationLimit);
                _ = sut.DecodeDictionary(
                    count => new Dictionary<int, long>(count),
                    (ref IceDecoder decoder) => decoder.DecodeInt(),
                    (ref IceDecoder decoder) => decoder.DecodeLong());
            },
            Throws.InstanceOf<InvalidDataException>());
    }

    [TestCase(10)]
    [TestCase(50)]
    [TestCase(100)]
    public void Decode_dictionary_within_max_collection_allocation(int count)
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[count * (Unsafe.SizeOf<int>() + Unsafe.SizeOf<long>()) + 256]);
        var encoder = new IceEncoder(buffer);
        var dict = Enumerable.Range(0, count).ToDictionary(k => k, v => (long)v);
        encoder.EncodeDictionary(
            dict,
            (ref IceEncoder encoder, int key) => encoder.EncodeInt(key),
            (ref IceEncoder encoder, long value) => encoder.EncodeLong(value));

        int allocationLimit = count * (Unsafe.SizeOf<int>() + Unsafe.SizeOf<long>());

        // Act/Assert
        Assert.That(
            () =>
            {
                var sut = new IceDecoder(buffer.WrittenMemory, maxCollectionAllocation: allocationLimit);
                _ = sut.DecodeDictionary(
                    count => new Dictionary<int, long>(count),
                    (ref IceDecoder decoder) => decoder.DecodeInt(),
                    (ref IceDecoder decoder) => decoder.DecodeLong());
            },
            Throws.Nothing);
    }

    /// <summary>Verifies that an encoded dictionary containing a duplicate key is rejected with
    /// <see cref="InvalidDataException" /> rather than surfacing the underlying <see cref="ArgumentException" />
    /// from <see cref="Dictionary{TKey,TValue}" />.</summary>
    [Test]
    public void Decode_dictionary_with_duplicate_key()
    {
        // Arrange: encode a "dictionary" with two entries sharing the same key.
        var buffer = new MemoryBufferWriter(new byte[64]);
        var encoder = new IceEncoder(buffer);
        encoder.EncodeSize(2);
        encoder.EncodeInt(42);
        encoder.EncodeString("first");
        encoder.EncodeInt(42);
        encoder.EncodeString("second");

        // Act/Assert
        Assert.That(
            () =>
            {
                var sut = new IceDecoder(buffer.WrittenMemory);
                _ = sut.DecodeDictionary(
                    count => new Dictionary<int, string>(count),
                    (ref IceDecoder decoder) => decoder.DecodeInt(),
                    (ref IceDecoder decoder) => decoder.DecodeString());
            },
            Throws.InstanceOf<InvalidDataException>()
                .With.Message.EqualTo("Received dictionary with duplicate key '42'."));
    }

    /// <summary>Verifies that a crafted count near int.MaxValue / entrySize that would overflow int arithmetic
    /// is correctly rejected by the allocation check.</summary>
    [Test]
    public void Decode_dictionary_with_overflowing_allocation_cost_is_rejected()
    {
        // Arrange
        // count * (sizeof(int) + sizeof(long)) would overflow in unchecked int arithmetic.
        int entrySize = Unsafe.SizeOf<int>() + Unsafe.SizeOf<long>();
        int count = (int.MaxValue / entrySize) + 1;
        var buffer = new MemoryBufferWriter(new byte[16]);
        var encoder = new IceEncoder(buffer);
        encoder.EncodeSize(count);

        // Act/Assert
        Assert.That(
            () =>
            {
                var sut = new IceDecoder(buffer.WrittenMemory, maxCollectionAllocation: 1024);
                _ = sut.DecodeDictionary(
                    count => new Dictionary<int, long>(count),
                    (ref IceDecoder decoder) => decoder.DecodeInt(),
                    (ref IceDecoder decoder) => decoder.DecodeLong());
            },
            Throws.InstanceOf<InvalidDataException>().With.Message.Contains("max collection allocation"));
    }
}
