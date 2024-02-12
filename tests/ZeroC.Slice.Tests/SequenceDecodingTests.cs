// Copyright (c) ZeroC, Inc.

using NUnit.Framework;
using System.Runtime.CompilerServices;

namespace ZeroC.Slice.Tests;

[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
[Parallelizable(scope: ParallelScope.All)]
public class SequenceDecodingTests
{
    [Test]
    public void Decode_bool_sequence_data_member([Values] SliceEncoding encoding)
    {
        // Arrange
        bool[] expected = [false, true, false];
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, encoding);
        encoder.EncodeSize(3);
        encoder.WriteByteSpan(new byte[] { 0x00, 0x01, 0x00 });
        var decoder = new SliceDecoder(buffer.WrittenMemory, encoding);

        // Act
        var sut = new BoolS(ref decoder);

        // Assert
        Assert.That(sut.Values, Is.EqualTo(expected));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Decode_bool_sequence_data_member_with_invalid_values([Values] SliceEncoding encoding)
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, encoding);
        encoder.EncodeSize(3);
        encoder.WriteByteSpan(new byte[] { 0x00, 0x01, 0x02 });

        // Act/Assert
        Assert.Throws<InvalidDataException>(
            () =>
            {
                var decoder = new SliceDecoder(buffer.WrittenMemory, encoding);
                var sut = new BoolS(ref decoder);
            });
    }

    /// <summary>Tests <see cref="SliceDecoderExtensions.DecodeSequence{T}(ref SliceDecoder, Action{T}?)" /> with a
    /// fixed-size numeric value type.</summary>
    /// <param name="encoding">The <see cref="SliceEncoding" /> to use for the decoding.</param>
    [Test]
    public void Decode_fixed_sized_numeric_sequence([Values] SliceEncoding encoding)
    {
        int[] expected = Enumerable.Range(0, 256).Select(i => i).ToArray();
        var buffer = new MemoryBufferWriter(new byte[1024 * 1024]);
        var encoder = new SliceEncoder(buffer, encoding);
        encoder.EncodeSize(expected.Length);
        foreach (int value in expected)
        {
            encoder.EncodeInt32(value);
        }
        var sut = new SliceDecoder(buffer.WrittenMemory, encoding);

        int[] result = sut.DecodeSequence((ref SliceDecoder decoder) => decoder.DecodeInt32());

        Assert.That(result, Is.EqualTo(expected));
        Assert.That(sut.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    /// <summary>Tests <see cref="SliceDecoderExtensions.DecodeSequence{T}(ref SliceDecoder, Action{T}?)" /> with a
    /// string sequence.</summary>
    /// <param name="encoding">The <see cref="SliceEncoding" /> to use for the decoding.</param>
    [Test]
    public void Decode_string_sequence([Values] SliceEncoding encoding)
    {
        string[] expected = Enumerable.Range(0, 256).Select(i => $"string-{i}").ToArray();
        var buffer = new MemoryBufferWriter(new byte[1024 * 1024]);
        var encoder = new SliceEncoder(buffer, encoding);
        encoder.EncodeSize(expected.Length);
        foreach (string value in expected)
        {
            encoder.EncodeString(value);
        }
        var sut = new SliceDecoder(buffer.WrittenMemory, encoding);

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
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
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
        var sut = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);

        // Act
        int?[] decoded = sut.DecodeSequenceOfOptionals<int?>((ref SliceDecoder decoder) => decoder.DecodeInt32());

        // Assert
        Assert.That(decoded, Is.EqualTo(expected));
    }

    [Test]
    public void Decode_sequence_with_bit_sequence_exceeds_default_max_collection_allocation()
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        long?[] seq = new long?[100];
        encoder.EncodeSequenceOfOptionals(
            seq,
            (ref SliceEncoder encoder, long? value) => encoder.EncodeInt64(value!.Value));

        Assert.That(
            () =>
            {
                var sut = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);
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
    public void Decode_sequence_with_enum_range_action()
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[1024 * 1024]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
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
        var sut = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);

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
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        long?[] seq = new long?[100];
        encoder.EncodeSequenceOfOptionals(
            seq,
            (ref SliceEncoder encoder, long? value) => encoder.EncodeInt64(value!.Value));

        Assert.That(
            () =>
            {
                var sut = new SliceDecoder(
                    buffer.WrittenMemory,
                    SliceEncoding.Slice2,
                    maxCollectionAllocation: seq.Length * Unsafe.SizeOf<long?>());
                _ = sut.DecodeSequenceOfOptionals<long?>((ref SliceDecoder decoder) => decoder.DecodeInt64());
            },
            Throws.Nothing);
    }
}
