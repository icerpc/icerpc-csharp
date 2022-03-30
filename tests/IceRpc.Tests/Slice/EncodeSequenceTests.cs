// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using NUnit.Framework;
using System.Buffers;
using System.Collections.Immutable;

namespace IceRpc.Slice.Tests;
[Parallelizable(scope: ParallelScope.All)]
public class EncodingSequenceTests
{
    /// <summary>Provides test case data for <see cref="EncodingSequence_Long(IEnumerable{long})"/> test.</summary>
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
                    yield return new TestCaseData(encoding, size, expected);
                }
            }
        }
    }

    private static byte[] Encoded_size_helper<T>(IEnumerable<T> v, SliceEncoding encoding)
    {
        var buffer = new byte[1024 * 1024];
        var bufferWriter = new MemoryBufferWriter(buffer);
        var encoder = new SliceEncoder(bufferWriter, encoding);

        encoder.EncodeSize(v.Count());

        return buffer[0..bufferWriter.WrittenMemory.Length];
    }

    /// <summary>Tests <see cref="SliceEncoderExtensions.EncodeSequence"/> and
    /// <see cref="SliceDecoderExtensions.DecodeSequence"/> with a value type. Includes testing
    /// the <see cref="T[]"/>, <see cref="ImmutableArray{T}"/>, and <see cref="ArraySegment{T}"/>
    /// cases for <see cref="SliceEncoderExtensions.EncodeSequence"/>. Finally, covers
    /// <see cref="SliceDecoder.DecodeLong"/>.</summary>
    /// <param name="p1">The IEnumerable<long> to be encoded.</param>
    [Test, TestCaseSource(nameof(SequenceLongData))]
    public void Encode_long_sequence(SliceEncoding encoding, IEnumerable<long> p1, byte[] expected)
    {
        var buffer = new byte[1024 * 1024];
        var bufferWriter = new MemoryBufferWriter(buffer);
        var encodedSizeBytes = Encoded_size_helper(p1, encoding);
        var sut = new SliceEncoder(bufferWriter, encoding);

        sut.EncodeSequence(p1);

        var encodedBytes = buffer[encodedSizeBytes.Length..bufferWriter.WrittenMemory.Length];
        Assert.That(encodedBytes, Is.EqualTo(expected));
        Assert.That(encodedBytes.Length, Is.EqualTo(sut.EncodedByteCount - encodedSizeBytes.Length));
    }

    /// <summary>Tests <see cref="SliceEncoderExtensions.EncodeSequence"/> and
    /// <see cref="SliceDecoderExtensions.DecodeSequence"/> with a reference type.
    /// Also tests <see cref="SliceDecoder.DecodeString"/>.</summary>
    /// <param name="size">An int used to specify how many elements to generate in the sequence.</param>
    [Test, TestCaseSource(nameof(EncodeStringSequenceDataSource))]
    public void Encode_string_sequence(SliceEncoding encoding, int size, byte[] expected)
    {
        var buffer = new byte[1024 * 1024];
        var bufferWriter = new MemoryBufferWriter(buffer);
        IEnumerable<string> strings = Enumerable.Range(0, size).Select(i => $"string-{i}");
        var encodedSizeBytes = Encoded_size_helper(strings, encoding);
        var sut = new SliceEncoder(bufferWriter, encoding);

        sut.EncodeSequence(strings, (ref SliceEncoder encoder, string value) => encoder.EncodeString(value));

        var encodedBytes = buffer[encodedSizeBytes.Length..bufferWriter.WrittenMemory.Length];
        Assert.That(encodedBytes, Is.EqualTo(expected));
        Assert.That(encodedBytes.Length, Is.EqualTo(sut.EncodedByteCount - encodedSizeBytes.Length));
    }
}
