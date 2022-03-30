// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using NUnit.Framework;
using System.Buffers;
using System.Collections.Immutable;

namespace IceRpc.Slice.Tests;
[Parallelizable(scope: ParallelScope.All)]
public class EncodingSequenceTests
{
    /// <summary>Provides test case data for
    /// <see cref="Encode_long_sequence(SliceEncoding, IEnumerable{long}, byte[])"/> test.</summary>
    private static IEnumerable<TestCaseData> SequenceLongData
    {
        get
        {
            foreach (SliceEncoding encoding in new SliceEncoding[] { SliceEncoding.Slice11, SliceEncoding.Slice20 })
            {
                foreach (int size in new int[] { 0, 256 })
                {
                    var longs = Enumerable.Range(0, size).Select(i => (long)i);
                    var buffer = new byte[1024 * 1024];
                    var bufferWriter = new MemoryBufferWriter(buffer);
                    longs.ToList().ForEach(l =>
                    {
                        var encoder = new SliceEncoder(bufferWriter, encoding);
                        encoder.EncodeLong(l);
                    });
                    byte[] expected = buffer[0..bufferWriter.WrittenMemory.Length];
                    yield return new TestCaseData(encoding, longs, expected);
                    yield return new TestCaseData(encoding, ImmutableArray.CreateRange(longs), expected);
                    yield return new TestCaseData(encoding, new ArraySegment<long>(longs.ToArray()), expected);
                    yield return new TestCaseData(encoding, longs.ToArray(), expected);
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
            foreach (SliceEncoding encoding in new SliceEncoding[] { SliceEncoding.Slice11, SliceEncoding.Slice20 })
            {
                foreach (int size in new int[] { 0, 256 })
                {
                    IEnumerable<string> strings = Enumerable.Range(0, size).Select(i => $"string-{i}");
                    var buffer = new byte[1024 * 1024];
                    var bufferWriter = new MemoryBufferWriter(buffer);
                    strings.ToList().ForEach(s =>
                    {
                        var encoder = new SliceEncoder(bufferWriter, encoding);
                        encoder.EncodeString(s);
                    });
                    byte[] expected = buffer[0..bufferWriter.WrittenMemory.Length];
                    yield return new TestCaseData(encoding, strings, expected);
                }
            }
        }
    }

    // <summary>A helper function that computes the encoded size bytes for any IEnumerable</summary>
    /// <param name="enumerable">The <see cref="IEnumerable{T}"/> to encode the size of.</param>
    /// <param name="encoding">The <see cref="SliceEncoding"/> to use for the size encoding.</param>
    private static byte[] Encoded_size_helper<T>(IEnumerable<T> enumerable, SliceEncoding encoding)
    {
        var buffer = new byte[1024 * 1024];
        var bufferWriter = new MemoryBufferWriter(buffer);
        var encoder = new SliceEncoder(bufferWriter, encoding);

        encoder.EncodeSize(enumerable.Count());

        return buffer[0..bufferWriter.WrittenMemory.Length];
    }

    /// <summary>Tests <see cref="SliceEncoderExtensions.EncodeSequence"/> and
    /// <see cref="SliceDecoderExtensions.DecodeSequence"/> with a value type. Includes testing
    /// the <see cref="T[]"/>, <see cref="ImmutableArray{T}"/>, and <see cref="ArraySegment{T}"/>
    /// cases for <see cref="SliceEncoderExtensions.EncodeSequence"/>. Finally, covers
    /// <see cref="SliceDecoder.DecodeLong"/>.</summary>
    /// <param name="encoding">The <see cref="SliceEncoding"/> to use for the encoding.</param>
    /// <param name="value">The <see cref="IEnumerable{long}"/> to be encoded.</param>
    /// <param name="expected">The expected byte array from encoding the sequence of longs</param>
    [Test, TestCaseSource(nameof(SequenceLongData))]
    public void Encode_long_sequence(SliceEncoding encoding, IEnumerable<long> value, byte[] expected)
    {
        var buffer = new byte[1024 * 1024];
        var bufferWriter = new MemoryBufferWriter(buffer);
        var encodedSizeBytes = Encoded_size_helper(value, encoding);
        var sut = new SliceEncoder(bufferWriter, encoding);

        sut.EncodeSequence(value);

        var encodedBytes = buffer[encodedSizeBytes.Length..bufferWriter.WrittenMemory.Length];
        Assert.That(encodedBytes, Is.EqualTo(expected));
        Assert.That(encodedBytes.Length, Is.EqualTo(sut.EncodedByteCount - encodedSizeBytes.Length));
    }

    /// <summary>Tests <see cref="SliceEncoderExtensions.EncodeSequence"/> and
    /// <see cref="SliceDecoderExtensions.DecodeSequence"/> with a reference type.
    /// Also tests <see cref="SliceDecoder.DecodeString"/>.</summary>
    /// <param name="encoding">The <see cref="SliceEncoding"/> to use for the encoding.</param>
    /// <param name="value">The <see cref="IEnumerable{string}"/> to be encoded.</param>
    /// <param name="expected">The expected byte array from encoding the sequence of strings</param>
    [Test, TestCaseSource(nameof(EncodeStringSequenceDataSource))]
    public void Encode_string_sequence(SliceEncoding encoding, IEnumerable<string> value, byte[] expected)
    {
        var buffer = new byte[1024 * 1024];
        var bufferWriter = new MemoryBufferWriter(buffer);
        var encodedSizeBytes = Encoded_size_helper(value, encoding);
        var sut = new SliceEncoder(bufferWriter, encoding);

        sut.EncodeSequence(value, (ref SliceEncoder encoder, string value) => encoder.EncodeString(value));

        var encodedBytes = buffer[encodedSizeBytes.Length..bufferWriter.WrittenMemory.Length];
        Assert.That(encodedBytes, Is.EqualTo(expected));
        Assert.That(encodedBytes.Length, Is.EqualTo(sut.EncodedByteCount - encodedSizeBytes.Length));
    }
}
