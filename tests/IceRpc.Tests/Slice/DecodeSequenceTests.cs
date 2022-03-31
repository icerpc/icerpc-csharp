// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using NUnit.Framework;
using System.Buffers;
using System.Collections.Immutable;

namespace IceRpc.Slice.Tests;

[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
[Parallelizable(scope: ParallelScope.All)]
public class DecodeSequenceTests
{
    /// <summary>Provides test case data for
    /// <see cref="Decode_long_sequence((SliceEncoding, byte[], IEnumerable{long})"/> test.</summary>
    private static IEnumerable<TestCaseData> SequenceLongData
    {
        get
        {
            foreach (SliceEncoding encoding in new SliceEncoding[] { SliceEncoding.Slice11, SliceEncoding.Slice20 })
            {
                foreach (int size in new int[] { 0, 256 })
                {
                    var expected = Enumerable.Range(0, size).Select(i => (long)i);
                    var buffer = new byte[1024 * 1024];
                    var bufferWriter = new MemoryBufferWriter(buffer);
                    var encoder = new SliceEncoder(bufferWriter, encoding);
                    encoder.EncodeSequence(expected);
                    yield return new TestCaseData(encoding, buffer[0..bufferWriter.WrittenMemory.Length], expected);
                };
            }
        }
    }

    /// <summary>Provides test case data for
    /// <see cref="Decode_string_sequence((SliceEncoding, byte[], IEnumerable{string})"/> test.</summary>
    private static IEnumerable<TestCaseData> SequenceStringData
    {
        get
        {
            foreach (SliceEncoding encoding in new SliceEncoding[] { SliceEncoding.Slice11, SliceEncoding.Slice20 })
            {
                foreach (int size in new int[] { 0, 256 })
                {
                    var expected = Enumerable.Range(0, size).Select(i => $"string-{i}");
                    var buffer = new byte[1024 * 1024];
                    var bufferWriter = new MemoryBufferWriter(buffer);
                    var encoder = new SliceEncoder(bufferWriter, encoding);
                    encoder.EncodeSequence(expected, (ref SliceEncoder encoder, string value) =>
                         encoder.EncodeString(value));
                    yield return new TestCaseData(encoding, buffer[0..bufferWriter.WrittenMemory.Length], expected);
                };
            }
        }
    }

    /// <summary>Tests <see cref="SliceDecoderExtensions.DecodeSequence"/> with a fixed-size numeric value type.
    /// </summary>
    /// <param name="encoding">The <see cref="SliceEncoding"/> to use for the decoding.</param>
    /// <param name="value">The byte array to decode.</param>
    /// <param name="expected">The expected <see cref="long[]"/> to be decoded</param>
    [Test, TestCaseSource(nameof(SequenceLongData))]
    public void Decode_long_sequence(SliceEncoding encoding, byte[] value, IEnumerable<long> expected)
    {
        var sut = new SliceDecoder(value, encoding);

        long[] result = sut.DecodeSequence(minElementSize: 1, (ref SliceDecoder decoder) => decoder.DecodeLong());

        Assert.That(result, Is.EqualTo(expected));
        Assert.That(sut.Consumed, Is.EqualTo(value.Length));
    }

    /// <summary>Tests <see cref="SliceDecoderExtensions.DecodeSequence"/> with a string.</summary>
    /// <param name="encoding">The <see cref="SliceEncoding"/> to use for the decoding.</param>
    /// <param name="value">The byte array to decode.</param>
    /// <param name="expected">The expected <see cref="string[]"/> to be decoded</param>
    [Test, TestCaseSource(nameof(SequenceStringData))]
    public void Decode_string_sequence(SliceEncoding encoding, byte[] value, IEnumerable<string> expected)
    {
        var sut = new SliceDecoder(value, encoding);

        string[] result = sut.DecodeSequence(minElementSize: 1, (ref SliceDecoder decoder) => decoder.DecodeString());

        Assert.That(result, Is.EqualTo(expected));
        Assert.That(sut.Consumed, Is.EqualTo(value.Length));
    }
}
