// Copyright (c) ZeroC, Inc.

using NUnit.Framework;

namespace ZeroC.Slice.Codec.Tests;

/// <summary>Test decoding of tagged fields.</summary>
[Parallelizable(scope: ParallelScope.All)]
public class TaggedDecodingTests
{
    /// <summary>Verifies that decoding a tagged value fails when the encoded data holds a negative tag other than the
    /// tag end marker.</summary>
    [Test]
    public void Decode_tagged_value_with_negative_encoded_tag_fails() =>
        Assert.That(
            () =>
            {
                // tag -2, size 1, value 42, tag end marker
                var sut = new SliceDecoder(new byte[] { 0xF8, 0x04, 0x2A, 0xFC });
                sut.DecodeTagged(0, (ref SliceDecoder decoder) => (byte?)decoder.DecodeUInt8());
            },
            Throws.TypeOf<InvalidDataException>());

    /// <summary>Verifies that skipping tagged fields fails when the encoded data holds a negative tag other than the
    /// tag end marker.</summary>
    [Test]
    public void Skip_tagged_values_with_negative_encoded_tag_fails() =>
        Assert.That(
            () =>
            {
                // tag -2, size 1, value 42, tag end marker
                var sut = new SliceDecoder(new byte[] { 0xF8, 0x04, 0x2A, 0xFC });
                sut.SkipTagged();
            },
            Throws.TypeOf<InvalidDataException>());
}
