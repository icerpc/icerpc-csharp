// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using NUnit.Framework;
using System.Buffers;
using System.Collections.Immutable;

namespace IceRpc.Slice.Tests;

[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
[TestFixture("1.1")]
[TestFixture("2.0")]
[Parallelizable(scope: ParallelScope.All)]
public class EncodingSequenceTests
{
    private readonly Memory<byte> _buffer;
    private readonly SliceEncoding _encoding;
    private readonly MemoryBufferWriter _bufferWriter;

    /// <summary>Provides test case data for <see cref="EncodingSequence_Long(IEnumerable<long>)"/> test.</summary>
    private static IEnumerable<TestCaseData> SequenceLongData
    {
        get
        {
            foreach (int size in new int[] { 0, 256 })
            {
                var p1 = Enumerable.Range(0, size).Select(i => (long)i);
                yield return new TestCaseData(p1);
                yield return new TestCaseData(ImmutableArray.CreateRange(p1));
                yield return new TestCaseData(new ArraySegment<long>(p1.ToArray()));
                yield return new TestCaseData(p1.ToArray());
            };
        }
    }

    public EncodingSequenceTests(string encoding)
    {
        _encoding = SliceEncoding.FromString(encoding);
        _buffer = new byte[1024 * 1024];
        _bufferWriter = new MemoryBufferWriter(_buffer);
    }

    /// <summary>Tests <see cref="SliceEncoderExtensions.EncodeSpan"/> and
    /// <see cref="SliceDecoderExtensions.DecodeSequence"/> with a fixed-size numeric value type.</summary>
    /// <param name="size">An int used to specify how many elements to generate in the sequence.</param>
    [TestCase(0)]
    [TestCase(256)]
    public void EncodingSequence_FixedSizeNumeric(int size)
    {
        var encoder = new SliceEncoder(_bufferWriter, _encoding);
        var decoder = new SliceDecoder(_buffer, _encoding);
        int[] p1 = Enumerable.Range(0, size).ToArray();

        encoder.EncodeSpan(new ReadOnlySpan<int>(p1));
        int[] r1 = decoder.DecodeSequence<int>();

        Assert.That(p1, Is.EqualTo(r1));
        Assert.That(decoder.Consumed, Is.EqualTo(_bufferWriter.WrittenMemory.Length));
        Assert.That(decoder.Consumed, Is.EqualTo(encoder.GetSizeLength(size) + size * sizeof(int)));
    }

    /// <summary>Tests <see cref="SliceEncoderExtensions.EncodeSequence"/> and
    /// <see cref="SliceDecoderExtensions.DecodeSequence"/> with a reference type.
    /// Also tests <see cref="SliceDecoder.DecodeString"/>.</summary>
    /// <param name="size">An int used to specify how many elements to generate in the sequence.</param>
    [TestCase(0)]
    [TestCase(256)]
    public void EncodingSequence_String(int size)
    {
        var encoder = new SliceEncoder(_bufferWriter, _encoding);
        var decoder = new SliceDecoder(_buffer, _encoding);
        IEnumerable<string> p1 = Enumerable.Range(0, size).Select(i => $"string-{i}");

        encoder.EncodeSequence(p1, (ref SliceEncoder encoder, string value) => encoder.EncodeString(value));
        IEnumerable<string> r1 = decoder.DecodeSequence(
            minElementSize: 1,
            (ref SliceDecoder decoder) => decoder.DecodeString());

        Assert.That(p1, Is.EqualTo(r1));
        Assert.That(decoder.Consumed, Is.EqualTo(_bufferWriter.WrittenMemory.Length));
    }

    /// <summary>Tests <see cref="SliceEncoderExtensions.EncodeSequence"/> and
    /// <see cref="SliceDecoderExtensions.DecodeSequence"/> with a value type. Includes testing
    /// the <see cref="T[]"/>, <see cref="ImmutableArray{T}"/>, and <see cref="ArraySegment{T}"/>
    /// cases for <see cref="SliceEncoderExtensions.EncodeSequence"/>. Finally, covers
    /// <see cref="SliceDecoder.DecodeLong"/>.</summary>
    /// <param name="p1">The IEnumerable<long> to be encoded.</param>
    [Test, TestCaseSource(nameof(SequenceLongData))]
    public void EncodingSequence_Long(IEnumerable<long> p1)
    {
        var encoder = new SliceEncoder(_bufferWriter, _encoding);
        var decoder = new SliceDecoder(_buffer, _encoding);

        encoder.EncodeSequence(p1);

        long[] r1 = decoder.DecodeSequence(minElementSize: 1, (ref SliceDecoder decoder) => decoder.DecodeLong());
        Assert.That(p1, Is.EqualTo(r1));
        Assert.That(decoder.Consumed, Is.EqualTo(_bufferWriter.WrittenMemory.Length));
    }
}
