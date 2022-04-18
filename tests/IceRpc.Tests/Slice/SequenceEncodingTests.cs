// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using NUnit.Framework;
using System.Buffers;
using System.Collections.Immutable;

namespace IceRpc.Slice.Tests;
[Parallelizable(scope: ParallelScope.All)]
public class SequenceEncodingTests
{
    /// <summary>Provides test case data for
    /// <see cref="Encode_fixed_sized_numeric_sequence(SliceEncoding, IEnumerable{long}, byte[])"/> test.</summary>
    private static IEnumerable<TestCaseData> SequenceLongData
    {
        get
        {
            foreach (SliceEncoding encoding in Enum.GetValues(typeof(SliceEncoding)))
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

    /// <summary>Tests <see cref="SliceEncoderExtensions.EncodeSequence"/> with a value type. Includes testing
    /// the <see cref="T[]"/>, <see cref="ImmutableArray{T}"/>, and <see cref="ArraySegment{T}"/>
    /// cases for <see cref="SliceEncoderExtensions.EncodeSequence"/>.</summary>
    /// <param name="encoding">The <see cref="SliceEncoding"/> to use for the encoding.</param>
    /// <param name="expected">The <see cref="IEnumerable{long}"/> to be encoded.</param>
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

    /// <summary>Tests <see cref="SliceEncoderExtensions.EncodeSequence"/> with a sequence of non numeric types.
    /// </summary>
    /// <param name="encoding">The <see cref="SliceEncoding"/> to use for the encoding.</param>
    [Test]
    public void Encode_string_sequence(
        [Values(SliceEncoding.Slice1, SliceEncoding.Slice2)] SliceEncoding encoding)
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
}
