// Copyright (c) ZeroC, Inc.

using NUnit.Framework;
using System.Runtime.CompilerServices;
using ZeroC.Tests.Common;

namespace ZeroC.Slice.Codec.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class SequenceDecodingTests
{
    /// <summary>Tests <see cref="SliceDecoderExtensions.DecodeSequence{T}(ref SliceDecoder, Action{T}?)" /> with a
    /// fixed-size numeric value type.</summary>
    [Test]
    public void Decode_fixed_sized_numeric_sequence()
    {
        // Arrange
        int[] expected = Enumerable.Range(0, 256).Select(i => i).ToArray();
        var buffer = new MemoryBufferWriter(new byte[1024 * 1024]);
        var encoder = new SliceEncoder(buffer);
        encoder.EncodeSize(expected.Length);
        foreach (int value in expected)
        {
            encoder.EncodeInt32(value);
        }
        var sut = new SliceDecoder(buffer.WrittenMemory);

        // Act
        int[] result = sut.DecodeSequence((ref SliceDecoder decoder) => decoder.DecodeInt32());

        // Assert
        Assert.That(result, Is.EqualTo(expected));
        Assert.That(sut.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    /// <summary>Tests <see cref="SliceDecoderExtensions.DecodeSequence{T}(ref SliceDecoder, Action{T}?)" /> with a
    /// string sequence.</summary>
    [Test]
    public void Decode_string_sequence()
    {
        // Arrange
        string[] expected = Enumerable.Range(0, 256).Select(i => $"string-{i}").ToArray();
        var buffer = new MemoryBufferWriter(new byte[1024 * 1024]);
        var encoder = new SliceEncoder(buffer);
        encoder.EncodeSize(expected.Length);
        foreach (string value in expected)
        {
            encoder.EncodeString(value);
        }
        var sut = new SliceDecoder(buffer.WrittenMemory);

        // Act
        string[] decoded = sut.DecodeSequence((ref SliceDecoder decoder) => decoder.DecodeString());

        // Assert
        Assert.That(decoded, Is.EqualTo(expected));
        Assert.That(sut.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Decode_sequence_of_optionals()
    {
        // Arrange
        int?[] expected = Enumerable.Range(0, 1024).Select(i => i % 2 == 0 ? (int?)i : null).ToArray();
        var buffer = new MemoryBufferWriter(new byte[1024 * 1024]);
        var encoder = new SliceEncoder(buffer);
        encoder.EncodeSize(expected.Length);
        BitSequenceWriter bitSequenceWriter = encoder.GetBitSequenceWriter(expected.Length);
        for (int i = 0; i < expected.Length; ++i)
        {
            int? value = expected[i];
            bitSequenceWriter.Write(value is not null);
            if (value is not null)
            {
                encoder.EncodeInt32(value.Value);
            }
        }
        var sut = new SliceDecoder(buffer.WrittenMemory);

        // Act
        int?[] decoded = sut.DecodeSequenceOfOptionals<int?>((ref SliceDecoder decoder) => decoder.DecodeInt32());

        // Assert
        Assert.That(decoded, Is.EqualTo(expected));
    }

    [TestCase(10)]
    [TestCase(50)]
    [TestCase(100)]
    public void Decode_sequence_with_bit_sequence_exceeds_max_collection_allocation(int count)
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[count * Unsafe.SizeOf<long?>() + 256]);
        var encoder = new SliceEncoder(buffer);
        long?[] seq = new long?[count];
        encoder.EncodeSequenceOfOptionals(
            seq,
            (ref SliceEncoder encoder, long? value) => encoder.EncodeInt64(value!.Value));

        int allocationLimit = (count - 1) * Unsafe.SizeOf<long?>();

        // Act/Assert
        Assert.That(
            () =>
            {
                var sut = new SliceDecoder(buffer.WrittenMemory, maxCollectionAllocation: allocationLimit);
                _ = sut.DecodeSequenceOfOptionals<long?>((ref SliceDecoder decoder) => decoder.DecodeInt64());
            },
            Throws.InstanceOf<InvalidDataException>());
    }

    private enum TestEnum : short
    {
        A = 1,
        B = 2,
        C = 3,
        D = 4,
    };

    [Test]
    public void Decode_sequence_with_element_action()
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[1024 * 1024]);
        var encoder = new SliceEncoder(buffer);
        var expected = new TestEnum[]
        {
            TestEnum.A,
            TestEnum.B,
            TestEnum.C,
            TestEnum.D,
        };

        // Encode the enumerators to a buffer
        encoder.EncodeSequence(
            expected,
            (ref SliceEncoder encoder, TestEnum value) => encoder.EncodeInt16((short)value));

        var checkedValues = new List<TestEnum>();
        var sut = new SliceDecoder(buffer.WrittenMemory);

        // Act
        TestEnum[]? decoded = sut.DecodeSequence<TestEnum>(value => checkedValues.Add(value));

        // Assert
        Assert.That(decoded, Is.EqualTo(expected));
        Assert.That(checkedValues, Is.EqualTo(expected));
    }

    [TestCase(10)]
    [TestCase(50)]
    [TestCase(100)]
    public void Decode_sequence_with_bit_sequence_within_max_collection_allocation(int count)
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[count * Unsafe.SizeOf<long?>() + 256]);
        var encoder = new SliceEncoder(buffer);
        long?[] seq = new long?[count];
        encoder.EncodeSequenceOfOptionals(
            seq,
            (ref SliceEncoder encoder, long? value) => encoder.EncodeInt64(value!.Value));

        int allocationLimit = count * Unsafe.SizeOf<long?>();

        // Act/Assert
        Assert.That(
            () =>
            {
                var sut = new SliceDecoder(buffer.WrittenMemory, maxCollectionAllocation: allocationLimit);
                _ = sut.DecodeSequenceOfOptionals<long?>((ref SliceDecoder decoder) => decoder.DecodeInt64());
            },
            Throws.Nothing);
    }

    [TestCase(10)]
    [TestCase(50)]
    [TestCase(100)]
    public void Decode_sequence_exceeds_max_collection_allocation(int count)
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[count * Unsafe.SizeOf<int>() + 256]);
        var encoder = new SliceEncoder(buffer);
        encoder.EncodeSequence(
            Enumerable.Range(0, count),
            (ref SliceEncoder encoder, int value) => encoder.EncodeInt32(value));

        int allocationLimit = (count - 1) * Unsafe.SizeOf<int>();

        // Act/Assert
        Assert.That(
            () =>
            {
                var sut = new SliceDecoder(buffer.WrittenMemory, maxCollectionAllocation: allocationLimit);
                _ = sut.DecodeSequence((ref SliceDecoder decoder) => decoder.DecodeInt32());
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
        var encoder = new SliceEncoder(buffer);
        encoder.EncodeSequence(
            Enumerable.Range(0, count),
            (ref SliceEncoder encoder, int value) => encoder.EncodeInt32(value));

        int allocationLimit = count * Unsafe.SizeOf<int>();

        // Act/Assert
        Assert.That(
            () =>
            {
                var sut = new SliceDecoder(buffer.WrittenMemory, maxCollectionAllocation: allocationLimit);
                _ = sut.DecodeSequence((ref SliceDecoder decoder) => decoder.DecodeInt32());
            },
            Throws.Nothing);
    }
}
