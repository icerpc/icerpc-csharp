// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using NUnit.Framework;

namespace IceRpc.Slice.Tests;

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

        int[] result = sut.DecodeSequence(minElementSize: 4, (ref SliceDecoder decoder) => decoder.DecodeInt32());

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

        string[] decoded = sut.DecodeSequence(minElementSize: 1, (ref SliceDecoder decoder) => decoder.DecodeString());

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
            bitSequenceWriter.Write(value != null);
            if (value != null)
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
}
