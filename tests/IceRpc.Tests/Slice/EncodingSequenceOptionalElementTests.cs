// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using NUnit.Framework;

namespace IceRpc.Slice.Tests;

[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
[TestFixture("2.0")]
[Parallelizable(scope: ParallelScope.All)]
public class EncodingSequenceOptionalElementsTests
{
    private readonly Memory<byte> _buffer;
    private readonly SliceEncoding _encoding;
    private readonly MemoryBufferWriter _bufferWriter;

    public EncodingSequenceOptionalElementsTests(string encoding)
    {
        _encoding = SliceEncoding.FromString(encoding);
        _buffer = new byte[1024 * 1024];
        _bufferWriter = new MemoryBufferWriter(_buffer);
    }

    /// <summary>Tests <see cref="SliceEncoderExtensions.EncodeSequenceWithBitSequence"/> with a fixed-size numeric
    /// value type. Tests <see cref="SliceDecoderExtensions.DecodeSequenceWithBitSequence"/> with a fixed-size
    /// numeric value type. Additionally, covers the case where count is 0 and the case where count > 0 for both
    /// <see cref="SliceEncoderExtensions.EncodeSequenceWithBitSequence"/> and
    /// <see cref="SliceDecoderExtensions.DecodeSequenceWithBitSequence"/>.</summary>
    /// <param name="size">An int used to specify how many elements to generate in the sequence.</param>
    [TestCase(0)]
    [TestCase(256)]
    public void EncodingSequence_FixedSizeNumeric_Optional(int size)
    {
        var encoder = new SliceEncoder(_bufferWriter, _encoding);
        var decoder = new SliceDecoder(_buffer, _encoding);
        IEnumerable<int?> p1 = Enumerable.Range(0, size).Select(i => (int?)(i % 2 == 0 ? null : i));

        encoder.EncodeSequenceWithBitSequence(
            p1,
            (ref SliceEncoder encoder, int? v) => encoder.EncodeInt(v!.Value));
        int?[] r1 = decoder.DecodeSequenceWithBitSequence(
            (ref SliceDecoder decoder) => decoder.DecodeInt() as int?);

        Assert.That(p1, Is.EqualTo(r1));
        Assert.That(decoder.Consumed, Is.EqualTo(_bufferWriter.WrittenMemory.Length));
    }

    /// <summary>Tests <see cref="SliceEncoderExtensions.EncodeSequenceWithBitSequence"/> with a reference type.
    /// Tests <see cref="SliceDecoderExtensions.DecodeSequenceWithBitSequence"/> with a reference type.
    /// Additionally, covers the case where count is 0 and the case where count > 0 for both
    /// <see cref="SliceEncoderExtensions.EncodeSequenceWithBitSequence"/> and
    /// <see cref="SliceDecoderExtensions.DecodeSequenceWithBitSequence"/>.</summary>
    /// <param name="size">An int used to specify how many elements to generate in the sequence.</param>
    [TestCase(0)]
    [TestCase(256)]
    public void EncodingSequence_String_Optional(int size)
    {
        var encoder = new SliceEncoder(_bufferWriter, _encoding);
        var decoder = new SliceDecoder(_buffer, _encoding);
        IEnumerable<string?> p1 = Enumerable
            .Range(0, size)
            .Select(i => i % 2 == 0 ? null : $"string-{i}");

        encoder.EncodeSequenceWithBitSequence(
            p1,
            (ref SliceEncoder encoder, string? value) => encoder.EncodeString(value!));
        string[] r1 = decoder.DecodeSequenceWithBitSequence((ref SliceDecoder decoder) => decoder.DecodeString());

        Assert.That(p1, Is.EqualTo(r1));
        Assert.That(decoder.Consumed, Is.EqualTo(_bufferWriter.WrittenMemory.Length));
    }

    /// <summary>Tests <see cref="SliceEncoderExtensions.EncodeSequenceWithBitSequence"/> with a reference type.
    /// Tests <see cref="SliceDecoderExtensions.DecodeSequenceWithBitSequence"/> with a reference type.
    /// Additionally, covers the case where count is 0 and the case where count > 0 for both
    /// <see cref="SliceEncoderExtensions.EncodeSequenceWithBitSequence"/> and
    /// <see cref="SliceDecoderExtensions.DecodeSequenceWithBitSequence"/>. Finally, covers the Sequence factory
    // constructor for <see cref="SliceDecoderExtensions.DecodeSequenceWithBitSequence"/>.</summary>
    /// <param name="size">An int used to specify how many elements to generate in the sequence.</param>
    [TestCase(0)]
    [TestCase(256)]
    public void EncodingSequence_String_Optional_Factory(int size)
    {
        var encoder = new SliceEncoder(_bufferWriter, _encoding);
        var decoder = new SliceDecoder(_buffer, _encoding);
        IEnumerable<string?> p1 = Enumerable
            .Range(0, size)
            .Select(i => (string?)(i % 2 == 0 ? null : $"string-{i}"));

        encoder.EncodeSequenceWithBitSequence(
            p1,
            (ref SliceEncoder encoder, string? value) => encoder.EncodeString(value!));

        List<string> r1 = decoder.DecodeSequenceWithBitSequence(
            i => new List<string>(i),
            (ref SliceDecoder decoder) => decoder.DecodeString());

        Assert.That(p1, Is.EqualTo(r1));
        Assert.That(decoder.Consumed, Is.EqualTo(_bufferWriter.WrittenMemory.Length));
    }
}
