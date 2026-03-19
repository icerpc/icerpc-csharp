// Copyright (c) ZeroC, Inc.

using NUnit.Framework;
using System.Buffers;
using System.Collections.Immutable;
using ZeroC.Tests.Common;

namespace IceRpc.Ice.Codec.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class SequenceEncodingTests
{
    /// <summary>Provides test case data for
    /// <see cref="Encode_fixed_sized_numeric_sequence(IEnumerable{int})" /> test.</summary>
    private static IEnumerable<TestCaseData> SequenceLongData
    {
        get
        {
            foreach (int size in new int[] { 0, 256 })
            {
                IEnumerable<int> values = Enumerable.Range(0, size).Select(i => i);

                yield return new TestCaseData(values);
                yield return new TestCaseData(ImmutableArray.CreateRange(values));
                yield return new TestCaseData(new ArraySegment<int>(values.ToArray()));
                yield return new TestCaseData(values.ToArray());
            };
        }
    }

    /// <summary>Tests <see cref="IceEncoderExtensions.EncodeSequence{T}(ref IceEncoder, IEnumerable{T})" /> with a
    /// value type.</summary>
    /// <param name="expected">The enumerable to be encoded.</param>
    [Test, TestCaseSource(nameof(SequenceLongData))]
    public void Encode_fixed_sized_numeric_sequence(IEnumerable<int> expected)
    {
        var buffer = new MemoryBufferWriter(new byte[1024 * 1024]);
        var sut = new IceEncoder(buffer);
        int size = expected.Count();

        sut.EncodeSequence(expected);

        var decoder = new IceDecoder(buffer.WrittenMemory);
        Assert.That(decoder.DecodeSize(), Is.EqualTo(size));
        var decoded = new List<int>();
        for (int i = 0; i < size; ++i)
        {
            decoded.Add(decoder.DecodeInt());
        }
        Assert.That(decoded, Is.EqualTo(expected));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    /// <summary>Tests <see cref="IceEncoderExtensions.EncodeSequence{T}(ref IceEncoder, IEnumerable{T},
    /// EncodeAction{T})" /> with a sequence of non numeric types.</summary>
    [Test]
    public void Encode_string_sequence()
    {
        var buffer = new MemoryBufferWriter(new byte[1024 * 1024]);
        var sut = new IceEncoder(buffer);
        string[] expected = Enumerable.Range(0, 1024).Select(i => $"value-{i}").ToArray();

        sut.EncodeSequence(expected, (ref IceEncoder encoder, string value) => encoder.EncodeString(value));

        var decoder = new IceDecoder(buffer.WrittenMemory);
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
