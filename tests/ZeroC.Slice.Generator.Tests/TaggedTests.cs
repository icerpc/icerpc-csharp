// Copyright (c) ZeroC, Inc.

using NUnit.Framework;
using ZeroC.Slice.Codec;
using ZeroC.Tests.Common;

namespace ZeroC.Slice.Generator.Tests;

internal class TaggedTests
{
    public static IEnumerable<TestCaseData> EncodeSliceTaggedFieldsSource
    {
        get
        {
            yield return new TestCaseData(_structWithTaggedFields[0]).SetName(
                "Encode_slice_tagged_fields(all_fields_set)");

            yield return new TestCaseData(_structWithTaggedFields[1]).SetName(
                "Encode_slice_tagged_fields(no_fields_set)");

            yield return new TestCaseData(_structWithTaggedFields[2]).SetName(
                "Encode_slice_tagged_fields(some_fields_set)");
        }
    }

    public static IEnumerable<TestCaseData> DecodeSliceTaggedFieldsSource
    {
        get
        {
            yield return new TestCaseData(_structWithTaggedFields[0]).SetName(
                "Decode_slice_tagged_fields(all_fields_set)");

            yield return new TestCaseData(_structWithTaggedFields[1]).SetName(
                "Decode_slice_tagged_fields(no_fields_set)");

            yield return new TestCaseData(_structWithTaggedFields[2]).SetName(
                "Decode_slice_tagged_fields(some_fields_set)");
        }
    }

    public static IEnumerable<TestCaseData> SkipSliceTaggedFieldsSource
    {
        get
        {
            yield return new TestCaseData(_structWithTaggedFields[0]).SetName(
                "Skip_slice_tagged_fields(all_fields_set)");

            yield return new TestCaseData(_structWithTaggedFields[1]).SetName(
                "Skip_slice_tagged_fields(no_fields_set)");

            yield return new TestCaseData(_structWithTaggedFields[2]).SetName(
                "Skip_slice_tagged_fields(some_fields_set)");
        }
    }

    private static readonly MyStructWithTaggedFields[] _structWithTaggedFields = new[]
    {
        new MyStructWithTaggedFields(
            10,
            new MyStruct(20, 20),
            MyEnum.Enum1,
            new byte[] { 1, 2, 3},
            "hello world!"),
        new MyStructWithTaggedFields(),
        new MyStructWithTaggedFields(
            10,
            null,
            MyEnum.Enum1,
            null,
            "hello world!"),
    };

    [Test, TestCaseSource(nameof(DecodeSliceTaggedFieldsSource))]
    public void Decode_slice_tagged_fields(MyStructWithTaggedFields expected)
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer);
        if (expected.A is byte a)
        {
            encoder.EncodeTagged(1, a, (ref SliceEncoder encoder, byte value) => encoder.EncodeUInt8(value));
        }
        if (expected.B is MyStruct b)
        {
            encoder.EncodeTagged(2, b, (ref SliceEncoder encoder, MyStruct value) => value.Encode(ref encoder));
        }
        if (expected.C is MyEnum c)
        {
            encoder.EncodeTagged(
                3,
                c,
                (ref SliceEncoder encoder, MyEnum value) => encoder.EncodeMyEnum(value));
        }
        if (expected.D is IList<byte> d)
        {
            encoder.EncodeTagged(4, d, (ref SliceEncoder encoder, IList<byte> value) => encoder.EncodeSequence(d));
        }
        if (expected.E is string e)
        {
            encoder.EncodeTagged(5, e, (ref SliceEncoder encoder, string value) => encoder.EncodeString(e));
        }
        encoder.EncodeVarInt32(SliceDefinitions.TagEndMarker);
        var decoder = new SliceDecoder(buffer.WrittenMemory);

        var decoded = new MyStructWithTaggedFields(ref decoder);
        Assert.That(decoded.A, Is.EqualTo(expected.A));
        Assert.That(decoded.B, Is.EqualTo(expected.B));
        Assert.That(decoded.C, Is.EqualTo(expected.C));
        Assert.That(decoded.D, Is.EqualTo(expected.D));
        Assert.That(decoded.E, Is.EqualTo(expected.E));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test, TestCaseSource(nameof(EncodeSliceTaggedFieldsSource))]
    public void Encode_slice_tagged_fields(MyStructWithTaggedFields expected)
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer);

        expected.Encode(ref encoder);

        var decoder = new SliceDecoder(buffer.WrittenMemory);

        Assert.That(
            decoder.DecodeTagged(1, (ref SliceDecoder decoder) => decoder.DecodeUInt8() as byte?),
            Is.EqualTo(expected.A));

        Assert.That(
            decoder.DecodeTagged(2, (ref SliceDecoder decoder) => new MyStruct(ref decoder) as MyStruct?),
            Is.EqualTo(expected.B));

        Assert.That(
            decoder.DecodeTagged(3, (ref SliceDecoder decoder) => decoder.DecodeMyEnum() as MyEnum?),
            Is.EqualTo(expected.C));

        Assert.That(
            decoder.DecodeTagged(4, (ref SliceDecoder decoder) => decoder.DecodeSequence<byte>()),
            Is.EqualTo(expected.D));

        Assert.That(
            decoder.DecodeTagged(5, (ref SliceDecoder decoder) => decoder.DecodeString()),
            Is.EqualTo(expected.E));

        Assert.That(decoder.DecodeVarInt32(), Is.EqualTo(SliceDefinitions.TagEndMarker));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test, TestCaseSource(nameof(SkipSliceTaggedFieldsSource))]
    public void Skip_slice_tagged_fields(MyStructWithTaggedFields value)
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer);
        value.Encode(ref encoder);

        var decoder = new SliceDecoder(buffer.WrittenMemory);

        // Act
        _ = new MyStructWithoutTaggedFields(ref decoder);

        // Assert
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }
}
