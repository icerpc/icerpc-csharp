// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Slice.Internal;
using NUnit.Framework;
using System.Runtime.CompilerServices;

namespace IceRpc.Tests.Slice;

[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
[Parallelizable(scope: ParallelScope.All)]
public class SequenceDecodingTests
{
    /// <summary>Tests <see cref="SliceDecoderExtensions.DecodeSequence"/> with a fixed-size numeric value type.
    /// </summary>
    /// <param name="encoding">The <see cref="SliceEncoding"/> to use for the decoding.</param>
    [Test]
    public void Decode_fixed_sized_numeric_sequence(
        [Values(SliceEncoding.Slice1, SliceEncoding.Slice2)] SliceEncoding encoding)
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

    /// <summary>Tests <see cref="SliceDecoderExtensions.DecodeSequence"/> with a string sequence.</summary>
    /// <param name="encoding">The <see cref="SliceEncoding"/> to use for the decoding.</param>
    [Test]
    public void Decode_string_sequence(
        [Values(SliceEncoding.Slice1, SliceEncoding.Slice2)] SliceEncoding encoding)
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
        int?[] decoded = sut.DecodeSequenceWithBitSequence<int?>((ref SliceDecoder decoder) => decoder.DecodeInt32());

        // Assert
        Assert.That(decoded, Is.EqualTo(expected));
    }

    [Test]
    public void Decode_sequence_with_bit_sequence_exceeds_default_max_collection_allocation()
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        long?[] seq = new long?[100];
        encoder.EncodeSequenceWithBitSequence(
            seq,
            (ref SliceEncoder encoder, long? value) => encoder.EncodeInt64(value!.Value));

        Assert.That(
            () =>
            {
                var sut = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);
                _ = sut.DecodeSequenceWithBitSequence<long?>((ref SliceDecoder decoder) => decoder.DecodeInt64());
            },
            Throws.InstanceOf<InvalidDataException>());
    }

    [Test]
    public void Decode_sequence_with_bit_sequence_and_custom_max_collection_allocation()
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        long?[] seq = new long?[100];
        encoder.EncodeSequenceWithBitSequence(
            seq,
            (ref SliceEncoder encoder, long? value) => encoder.EncodeInt64(value!.Value));

        Assert.That(
            () =>
            {
                var sut = new SliceDecoder(
                    buffer.WrittenMemory,
                    SliceEncoding.Slice2,
                    maxCollectionAllocation: seq.Length * Unsafe.SizeOf<long?>());
                _ = sut.DecodeSequenceWithBitSequence<long?>((ref SliceDecoder decoder) => decoder.DecodeInt64());
            },
            Throws.Nothing);
    }
}
