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
        int[] expected = Enumerable.Range(0, 256).Select(i => i).ToArray();
        var buffer = new MemoryBufferWriter(new byte[1024 * 1024]);
        var encoder = new SliceEncoder(buffer);
        encoder.EncodeSize(expected.Length);
        foreach (int value in expected)
        {
            encoder.EncodeInt32(value);
        }
        var sut = new SliceDecoder(buffer.WrittenMemory);

        int[] result = sut.DecodeSequence((ref SliceDecoder decoder) => decoder.DecodeInt32());

        Assert.That(result, Is.EqualTo(expected));
        Assert.That(sut.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    /// <summary>Tests <see cref="SliceDecoderExtensions.DecodeSequence{T}(ref SliceDecoder, Action{T}?)" /> with a
    /// string sequence.</summary>
    [Test]
    public void Decode_string_sequence()
    {
        string[] expected = Enumerable.Range(0, 256).Select(i => $"string-{i}").ToArray();
        var buffer = new MemoryBufferWriter(new byte[1024 * 1024]);
        var encoder = new SliceEncoder(buffer);
        encoder.EncodeSize(expected.Length);
        foreach (string value in expected)
        {
            encoder.EncodeString(value);
        }
        var sut = new SliceDecoder(buffer.WrittenMemory);

        string[] decoded = sut.DecodeSequence((ref SliceDecoder decoder) => decoder.DecodeString());

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

    [Test]
    public void Decode_sequence_with_bit_sequence_exceeds_default_max_collection_allocation()
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer);
        long?[] seq = new long?[100];
        encoder.EncodeSequenceOfOptionals(
            seq,
            (ref SliceEncoder encoder, long? value) => encoder.EncodeInt64(value!.Value));

        Assert.That(
            () =>
            {
                var sut = new SliceDecoder(buffer.WrittenMemory);
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

    // This test is unique to the Slice encoding where enums can use a fixed number of bytes.
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

    [Test]
    public void Decode_sequence_with_bit_sequence_and_custom_max_collection_allocation()
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer);
        long?[] seq = new long?[100];
        encoder.EncodeSequenceOfOptionals(
            seq,
            (ref SliceEncoder encoder, long? value) => encoder.EncodeInt64(value!.Value));

        Assert.That(
            () =>
            {
                var sut = new SliceDecoder(
                    buffer.WrittenMemory,
                    maxCollectionAllocation: seq.Length * Unsafe.SizeOf<long?>());
                _ = sut.DecodeSequenceOfOptionals<long?>((ref SliceDecoder decoder) => decoder.DecodeInt64());
            },
            Throws.Nothing);
    }
}
