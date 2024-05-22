// Copyright (c) ZeroC, Inc.

using NUnit.Framework;
using System.Buffers;
using System.Collections.Immutable;

namespace ZeroC.Slice.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class SequenceEncodingTests
{
    /// <summary>Provides test case data for
    /// <see cref="Encode_fixed_sized_numeric_sequence(SliceEncoding, IEnumerable{int})" /> test.</summary>
    private static IEnumerable<TestCaseData> SequenceLongData
    {
        get
        {
            foreach (SliceEncoding encoding in Enum.GetValues<SliceEncoding>())
            {
                foreach (int size in new int[] { 0, 256 })
                {
                    IEnumerable<int> values = Enumerable.Range(0, size).Select(i => i);

                    yield return new TestCaseData(encoding, values);
                    yield return new TestCaseData(encoding, ImmutableArray.CreateRange(values));
                    yield return new TestCaseData(encoding, new ArraySegment<int>(values.ToArray()));
                    yield return new TestCaseData(encoding, values.ToArray());
                };
            }
        }
    }

    /// <summary>Tests <see cref="SliceEncoderExtensions.EncodeSequence{T}(ref SliceEncoder, IEnumerable{T})" /> with a
    /// value type.</summary>
    /// <param name="encoding">The <see cref="SliceEncoding" /> to use for the encoding.</param>
    /// <param name="expected">The enumerable to be encoded.</param>
    [Test, TestCaseSource(nameof(SequenceLongData))]
    public void Encode_fixed_sized_numeric_sequence(SliceEncoding encoding, IEnumerable<int> expected)
    {
        var buffer = new MemoryBufferWriter(new byte[1024 * 1024]);
        var sut = new SliceEncoder(buffer, encoding);
        int size = expected.Count();

        sut.EncodeSequence(expected);

        var decoder = new SliceDecoder(buffer.WrittenMemory, encoding);
        Assert.That(decoder.DecodeSize(), Is.EqualTo(size));
        var decoded = new List<int>();
        for (int i = 0; i < size; ++i)
        {
            decoded.Add(decoder.DecodeInt32());
        }
        Assert.That(decoded, Is.EqualTo(expected));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    /// <summary>Tests <see cref="SliceEncoderExtensions.EncodeSequence{T}(ref SliceEncoder, IEnumerable{T},
    /// EncodeAction{T})" /> with a sequence of non numeric types.</summary>
    /// <param name="encoding">The <see cref="SliceEncoding" /> to use for the encoding.</param>
    [Test]
    public void Encode_string_sequence([Values] SliceEncoding encoding)
    {
        var buffer = new MemoryBufferWriter(new byte[1024 * 1024]);
        var sut = new SliceEncoder(buffer, encoding);
        string[] expected = Enumerable.Range(0, 1024).Select(i => $"value-{i}").ToArray();

        sut.EncodeSequence(expected, (ref SliceEncoder encoder, string value) => encoder.EncodeString(value));

        var decoder = new SliceDecoder(buffer.WrittenMemory, encoding);
        Assert.That(decoder.DecodeSize(), Is.EqualTo(expected.Length));
        var decoded = new List<string>();
        for (int i = 0; i < expected.Length; ++i)
        {
            decoded.Add(decoder.DecodeString());
        }
        Assert.That(decoded, Is.EqualTo(expected));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Encode_sequence_of_optionals()
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[1024 * 1024]);
        var sut = new SliceEncoder(buffer, SliceEncoding.Slice2);
        int?[] expected = Enumerable.Range(0, 1024).Select(i => i % 2 == 0 ? (int?)i : null).ToArray();

        // Act
        sut.EncodeSequenceOfOptionals(
            expected,
            (ref SliceEncoder encoder, int? value) => encoder.EncodeInt32(value!.Value));

        // Assert
        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);
        Assert.That(decoder.DecodeSize(), Is.EqualTo(expected.Length));
        var bitSequenceReader = decoder.GetBitSequenceReader(expected.Length);
        for (int i = 0; i < expected.Length; ++i)
        {
            if (bitSequenceReader.Read())
            {
                Assert.That(decoder.DecodeInt32(), Is.EqualTo(expected[i]));
            }
            else
            {
                Assert.That(expected[i], Is.Null);
            }
        }
    }
}
