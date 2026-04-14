// Copyright (c) ZeroC, Inc.

using NUnit.Framework;
using System.Runtime.CompilerServices;
using ZeroC.Tests.Common;

namespace IceRpc.Ice.Codec.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class SequenceDecodingTests
{
    /// <summary>Tests <see cref="IceDecoderExtensions.DecodeSequence{T}(ref IceDecoder, DecodeFunc{T})" /> with a
    /// fixed-size numeric value type.</summary>
    [Test]
    public void Decode_fixed_sized_numeric_sequence()
    {
        // Arrange
        int[] expected = Enumerable.Range(0, 256).Select(i => i).ToArray();
        var buffer = new MemoryBufferWriter(new byte[1024 * 1024]);
        var encoder = new IceEncoder(buffer);
        encoder.EncodeSize(expected.Length);
        foreach (int value in expected)
        {
            encoder.EncodeInt(value);
        }
        var sut = new IceDecoder(buffer.WrittenMemory);

        // Act
        int[] result = sut.DecodeSequence((ref IceDecoder decoder) => decoder.DecodeInt());

        // Assert
        Assert.That(result, Is.EqualTo(expected));
        Assert.That(sut.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    /// <summary>Tests <see cref="IceDecoderExtensions.DecodeSequence{T}(ref IceDecoder, DecodeFunc{T})" /> with a
    /// string sequence.</summary>
    [Test]
    public void Decode_string_sequence()
    {
        // Arrange
        string[] expected = Enumerable.Range(0, 256).Select(i => $"string-{i}").ToArray();
        var buffer = new MemoryBufferWriter(new byte[1024 * 1024]);
        var encoder = new IceEncoder(buffer);
        encoder.EncodeSize(expected.Length);
        foreach (string value in expected)
        {
            encoder.EncodeString(value);
        }
        var sut = new IceDecoder(buffer.WrittenMemory);

        // Act
        string[] decoded = sut.DecodeSequence((ref IceDecoder decoder) => decoder.DecodeString());

        // Assert
        Assert.That(decoded, Is.EqualTo(expected));
        Assert.That(sut.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Decode_sequence_with_element_action()
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[1024 * 1024]);
        var encoder = new IceEncoder(buffer);
        var expected = new bool[] { true, false, true, false, false, true, true, false };

        // Encode the enumerators to a buffer
        encoder.EncodeSequence(
            expected,
            (ref IceEncoder encoder, bool value) => encoder.EncodeBool(value));

        var checkedValues = new List<bool>();
        var sut = new IceDecoder(buffer.WrittenMemory);

        // Act
        bool[]? decoded = sut.DecodeSequence<bool>(value => checkedValues.Add(value));

        // Assert
        Assert.That(decoded, Is.EqualTo(expected));
        Assert.That(checkedValues, Is.EqualTo(expected));
    }

    [TestCase(10)]
    [TestCase(50)]
    [TestCase(100)]
    public void Decode_sequence_exceeds_max_collection_allocation(int count)
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[count * Unsafe.SizeOf<int>() + 256]);
        var encoder = new IceEncoder(buffer);
        encoder.EncodeSequence(
            Enumerable.Range(0, count),
            (ref IceEncoder encoder, int value) => encoder.EncodeInt(value));

        int allocationLimit = (count - 1) * Unsafe.SizeOf<int>();

        // Act/Assert
        Assert.That(
            () =>
            {
                var sut = new IceDecoder(buffer.WrittenMemory, maxCollectionAllocation: allocationLimit);
                _ = sut.DecodeSequence((ref IceDecoder decoder) => decoder.DecodeInt());
            },
            Throws.InstanceOf<InvalidDataException>());
    }

    [TestCase(10)]
    [TestCase(50)]
    [TestCase(100)]
    public void Decode_sequence_within_max_collection_allocation(int count)
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[count * Unsafe.SizeOf<int>() + 256]);
        var encoder = new IceEncoder(buffer);
        encoder.EncodeSequence(
            Enumerable.Range(0, count),
            (ref IceEncoder encoder, int value) => encoder.EncodeInt(value));

        int allocationLimit = count * Unsafe.SizeOf<int>();

        // Act/Assert
        Assert.That(
            () =>
            {
                var sut = new IceDecoder(buffer.WrittenMemory, maxCollectionAllocation: allocationLimit);
                _ = sut.DecodeSequence((ref IceDecoder decoder) => decoder.DecodeInt());
            },
            Throws.Nothing);
    }

    /// <summary>Verifies that a crafted count near int.MaxValue / elementSize that would overflow int arithmetic
    /// is correctly rejected by the allocation check.</summary>
    [Test]
    public void Decode_sequence_with_overflowing_allocation_cost_is_rejected()
    {
        // Arrange
        // count * sizeof(int) would overflow in unchecked int arithmetic.
        int count = (int.MaxValue / Unsafe.SizeOf<int>()) + 1;
        var buffer = new MemoryBufferWriter(new byte[16]);
        var encoder = new IceEncoder(buffer);
        encoder.EncodeSize(count);

        // Act/Assert
        Assert.That(
            () =>
            {
                var sut = new IceDecoder(buffer.WrittenMemory, maxCollectionAllocation: 1024);
                _ = sut.DecodeSequence((ref IceDecoder decoder) => decoder.DecodeInt());
            },
            Throws.InstanceOf<InvalidDataException>().With.Message.Contains("max collection allocation"));
    }
}
