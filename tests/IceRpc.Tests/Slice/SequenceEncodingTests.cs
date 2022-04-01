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
                    var values = Enumerable.Range(0, size).Select(i => (long)i);
                    var buffer = new byte[1024 * 1024];
                    var bufferWriter = new MemoryBufferWriter(buffer);
                    var encoder = new SliceEncoder(bufferWriter, encoding);
                    foreach (long value in values)
                    {
                        encoder.EncodeLong(value);
                    }
                    int sizeLength = encoder.GetSizeLength(size);
                    byte[] expected = buffer[0..bufferWriter.WrittenMemory.Length];
                    yield return new TestCaseData(encoding, values, expected, sizeLength);
                    yield return new TestCaseData(encoding, ImmutableArray.CreateRange(values), expected, sizeLength);
                    yield return new TestCaseData(
                        encoding,
                        new ArraySegment<long>(values.ToArray()),
                        expected,
                        sizeLength);
                    yield return new TestCaseData(encoding, values.ToArray(), expected, sizeLength);
                };
            }
        }
    }

    /// <summary>Provides test case data for <see cref="Encode_string_sequence(SliceEncoding, int, byte[])"/> test.
    /// </summary>
    private static IEnumerable<TestCaseData> EncodeStringSequenceDataSource
    {
        get
        {
            foreach (SliceEncoding encoding in Enum.GetValues(typeof(SliceEncoding)))
            {
                foreach (int size in new int[] { 0, 256 })
                {
                    IEnumerable<string> values = Enumerable.Range(0, size).Select(i => $"string-{i}");
                    var buffer = new byte[1024 * 1024];
                    var bufferWriter = new MemoryBufferWriter(buffer);
                    var encoder = new SliceEncoder(bufferWriter, encoding);
                    foreach (string value in values)
                    {
                        encoder.EncodeString(value);
                    }
                    int sizeLength = encoder.GetSizeLength(size);
                    byte[] expected = buffer[0..bufferWriter.WrittenMemory.Length];
                    yield return new TestCaseData(encoding, values, expected, sizeLength);
                }
            }
        }
    }

    /// <summary>Tests <see cref="SliceEncoderExtensions.EncodeSequence"/> and
    /// <see cref="SliceDecoderExtensions.DecodeSequence"/> with a value type. Includes testing
    /// the <see cref="T[]"/>, <see cref="ImmutableArray{T}"/>, and <see cref="ArraySegment{T}"/>
    /// cases for <see cref="SliceEncoderExtensions.EncodeSequence"/>. Finally, covers
    /// <see cref="SliceDecoder.DecodeLong"/>.</summary>
    /// <param name="encoding">The <see cref="SliceEncoding"/> to use for the encoding.</param>
    /// <param name="value">The <see cref="IEnumerable{long}"/> to be encoded.</param>
    /// <param name="expected">The expected byte array from encoding the sequence of longs</param>
    /// <param name="sizeLength">The number of bytes required to encode the sequence size.</param>
    [Test, TestCaseSource(nameof(SequenceLongData))]
    public void Encode_fixed_sized_numeric_sequence(
        SliceEncoding encoding,
        IEnumerable<long> value,
        byte[] expected,
        int sizeLength)
    {
        var bufferWriter = new MemoryBufferWriter(new byte[1024 * 1024]);
        var sut = new SliceEncoder(bufferWriter, encoding);

        sut.EncodeSequence(value);

        var encoded = bufferWriter.WrittenMemory[sizeLength..].ToArray();
        Assert.That(encoded, Is.EqualTo(expected.ToArray()));
        Assert.That(bufferWriter.WrittenMemory.Length, Is.EqualTo(sut.EncodedByteCount));
    }

    /// <summary>Tests <see cref="SliceEncoderExtensions.EncodeSequence"/> and
    /// <see cref="SliceDecoderExtensions.DecodeSequence"/> with a reference type.
    /// Also tests <see cref="SliceDecoder.DecodeString"/>.</summary>
    /// <param name="encoding">The <see cref="SliceEncoding"/> to use for the encoding.</param>
    /// <param name="value">The <see cref="IEnumerable{string}"/> to be encoded.</param>
    /// <param name="expected">The expected byte array from encoding the sequence of strings</param>
    /// <param name="sizeLength">The number of bytes required to encode the sequence size.</param>
    [Test, TestCaseSource(nameof(EncodeStringSequenceDataSource))]
    public void Encode_string_sequence(
        SliceEncoding encoding, 
        IEnumerable<string> value,
        byte[] expected,
        int sizeLength)
    {
        var bufferWriter = new MemoryBufferWriter(new byte[1024 * 1024]);
        var sut = new SliceEncoder(bufferWriter, encoding);

        sut.EncodeSequence(value, (ref SliceEncoder encoder, string value) => encoder.EncodeString(value));

        byte[] encoded = bufferWriter.WrittenMemory[sizeLength..].ToArray();
        Assert.That(encoded, Is.EqualTo(expected));
        Assert.That(encoded.Length, Is.EqualTo(sut.EncodedByteCount - sizeLength));
    }
}
