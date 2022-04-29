// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using NUnit.Framework;

namespace IceRpc.Slice.Tagged.Slice1.Tests;

public class TaggedTests
{
    public static IEnumerable<TestCaseData> EncodeSlice1TagggedMembersSource
    {
        get
        {
            yield return new TestCaseData(_classWithTaggedMembers[0]).SetName(
                "Encode_slice1_tagged_members(all_members_set)");

            yield return new TestCaseData(_classWithTaggedMembers[1]).SetName(
                "Encode_slice1_tagged_members(no_members_set)");

            yield return new TestCaseData(_classWithTaggedMembers[2]).SetName(
                "Encode_slice1_tagged_members(some_members_set)");
        }
    }

    public static IEnumerable<TestCaseData> DecodeSlice1TagggedMembersSource
    {
        get
        {
            yield return new TestCaseData(_classWithTaggedMembers[0]).SetName(
                "Decode_slice1_tagged_members(all_members_set)");

            yield return new TestCaseData(_classWithTaggedMembers[1]).SetName(
                "Decode_slice1_tagged_members(no_members_set)");

            yield return new TestCaseData(_classWithTaggedMembers[2]).SetName(
                "Decode_slice1_tagged_members(some_members_set)");
        }
    }

    private static readonly ClassWithTaggedMembers[] _classWithTaggedMembers = new ClassWithTaggedMembers[]
    {
        new ClassWithTaggedMembers(
                    10,
                    20,
                    30,
                    40,
                    new FixedLengthStruct(1, 1),
                    new VarLengthStruct("hello world!"),
                    MyEnum.Two,
                    new byte[] { 1, 2, 3 },
                    new int[] { 4, 5, 6 },
                    "hello world!"),
        new ClassWithTaggedMembers(),
        new ClassWithTaggedMembers(
                    10,
                    null,
                    30,
                    null,
                    new FixedLengthStruct(1, 1),
                    null,
                    MyEnum.Two,
                    null,
                    new int[] { 4, 5, 6 },
                    null)
    };

    [Test, TestCaseSource(nameof(DecodeSlice1TagggedMembersSource))]
    public void Decode_slice1_tagged_members(ClassWithTaggedMembers expected)
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice1);
        bool hasTaggedMembers =
            expected.A != null ||
            expected.B != null ||
            expected.D != null ||
            expected.E != null ||
            expected.F != null ||
            expected.G != null ||
            expected.H != null ||
            expected.I != null ||
            expected.J != null;

        encoder.EncodeSize(1); // Instance marker
        byte flags = (byte)Slice1Definitions.TypeIdKind.String | (byte)Slice1Definitions.SliceFlags.IsLastSlice;
        if (hasTaggedMembers)
        {
            flags |= (byte)Slice1Definitions.SliceFlags.HasTaggedMembers;
        }
        encoder.EncodeUInt8(flags);
        encoder.EncodeString(ClassWithTaggedMembers.SliceTypeId);

        if (expected.A != null)
        {
            encoder.EncodeTagged(
                1,
                TagFormat.F1,
                expected.A.Value,
                (ref SliceEncoder encoder, byte value) => encoder.EncodeUInt8(value));
        }

        if (expected.B != null)
        {
            encoder.EncodeTagged(
                2,
                TagFormat.F2,
                expected.B.Value,
                (ref SliceEncoder encoder, short value) => encoder.EncodeInt16(value));
        }

        if (expected.C != null)
        {
            encoder.EncodeTagged(
                3,
                TagFormat.F4,
                expected.C.Value,
                (ref SliceEncoder encoder, int value) => encoder.EncodeInt32(value));
        }

        if (expected.D != null)
        {
            encoder.EncodeTagged(
                4,
                TagFormat.F8,
                expected.D.Value,
                (ref SliceEncoder encoder, long value) => encoder.EncodeInt64(value));
        }

        if (expected.E != null)
        {
            encoder.EncodeTagged(
                5,
                size: 8,
                expected.E.Value,
                (ref SliceEncoder encoder, FixedLengthStruct value) => value.Encode(ref encoder));
        }

        if (expected.F != null)
        {
            encoder.EncodeTagged(
                6,
                TagFormat.FSize,
                expected.F.Value,
                (ref SliceEncoder encoder, VarLengthStruct value) => value.Encode(ref encoder));
        }

        if (expected.G != null)
        {
            encoder.EncodeTagged(
                7,
                TagFormat.Size,
                expected.G.Value,
                (ref SliceEncoder encoder, MyEnum value) => encoder.EncodeMyEnum(value));
        }

        if (expected.H != null)
        {
            encoder.EncodeTagged(
                8,
                TagFormat.OVSize,
                expected.H,
                (ref SliceEncoder encoder, IList<byte> value) => encoder.EncodeSequence(value));
        }

        if (expected.I != null)
        {
            encoder.EncodeTagged(
                9,
                size: encoder.GetSizeLength(expected.I.Count) + (4 * expected.I.Count),
                expected.I,
                (ref SliceEncoder encoder, IList<int> value) => encoder.EncodeSequence(value));
        }

        if (expected.J != null)
        {
            encoder.EncodeTagged(
                10,
                TagFormat.OVSize,
                expected.J,
                (ref SliceEncoder encoder, string value) => encoder.EncodeString(value));
        }

        if (hasTaggedMembers)
        {
            encoder.EncodeUInt8(Slice1Definitions.TagEndMarker);
        }
        var decoder = new SliceDecoder(
            buffer.WrittenMemory,
            SliceEncoding.Slice1,
            activator: SliceDecoder.GetActivator(typeof(ClassWithTaggedMembers).Assembly));

        // Act
        var c = decoder.DecodeClass<ClassWithTaggedMembers>();

        // Assert
        Assert.That(c.A, Is.EqualTo(expected.A));
        Assert.That(c.B, Is.EqualTo(expected.B));
        Assert.That(c.C, Is.EqualTo(expected.C));
        Assert.That(c.D, Is.EqualTo(expected.D));
        Assert.That(c.E, Is.EqualTo(expected.E));
        Assert.That(c.F, Is.EqualTo(expected.F));
        Assert.That(c.G, Is.EqualTo(expected.G));
        Assert.That(c.H, Is.EqualTo(expected.H));
        Assert.That(c.I, Is.EqualTo(expected.I));
        Assert.That(c.J, Is.EqualTo(expected.J));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test, TestCaseSource(nameof(EncodeSlice1TagggedMembersSource))]
    public void Encode_slice1_tagged_members(ClassWithTaggedMembers c)
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice1);

        // Act
        encoder.EncodeClass(c);

        // Assert
        bool hasTaggedMembers =
            c.A != null ||
            c.B != null ||
            c.D != null ||
            c.E != null ||
            c.F != null ||
            c.G != null ||
            c.H != null ||
            c.I != null ||
            c.J != null;

        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice1);

        Assert.That(decoder.DecodeSize(), Is.EqualTo(1)); // Instance marker
        byte flags = (byte)Slice1Definitions.TypeIdKind.String | (byte)Slice1Definitions.SliceFlags.IsLastSlice;
        if (hasTaggedMembers)
        {
            flags |= (byte)Slice1Definitions.SliceFlags.HasTaggedMembers;
        }
        Assert.That(decoder.DecodeUInt8(), Is.EqualTo(flags));

        Assert.That(decoder.DecodeString(), Is.EqualTo(ClassWithTaggedMembers.SliceTypeId));

        if (c.A != null)
        {
            Assert.That(
                decoder.DecodeTagged(
                    1,
                    TagFormat.F1,
                    (ref SliceDecoder decoder) => decoder.DecodeUInt8(),
                    useTagEndMarker: false),
                Is.EqualTo(c.A.Value));
        }

        if (c.B != null)
        {
            Assert.That(
                decoder.DecodeTagged(
                    2,
                    TagFormat.F2,
                    (ref SliceDecoder decoder) => decoder.DecodeInt16(),
                    useTagEndMarker: false),
                Is.EqualTo(c.B.Value));
        }

        if (c.C != null)
        {
            Assert.That(
                decoder.DecodeTagged(
                    3,
                    TagFormat.F4,
                    (ref SliceDecoder decoder) => decoder.DecodeInt32(),
                    useTagEndMarker: false),
                Is.EqualTo(c.C.Value));
        }

        if (c.D != null)
        {
            Assert.That(
                decoder.DecodeTagged(
                    4,
                    TagFormat.F8,
                    (ref SliceDecoder decoder) => decoder.DecodeInt64(),
                    useTagEndMarker: false),
                Is.EqualTo(c.D.Value));
        }

        if (c.E != null)
        {
            Assert.That(
                decoder.DecodeTagged(
                    5,
                    TagFormat.VSize,
                    (ref SliceDecoder decoder) => new FixedLengthStruct(ref decoder),
                    useTagEndMarker: false),
                Is.EqualTo(c.E.Value));
        }

        if (c.F != null)
        {
            Assert.That(
                decoder.DecodeTagged(
                    6,
                    TagFormat.FSize,
                    (ref SliceDecoder decoder) => new VarLengthStruct(ref decoder),
                    useTagEndMarker: false),
                Is.EqualTo(c.F.Value));
        }

        if (c.G != null)
        {
            Assert.That(
                decoder.DecodeTagged(
                    7,
                    TagFormat.Size,
                    (ref SliceDecoder decoder) => decoder.DecodeMyEnum(),
                    useTagEndMarker: false),
                Is.EqualTo(c.G.Value));
        }

        if (c.H != null)
        {
            Assert.That(
                decoder.DecodeTagged(
                    8,
                    TagFormat.OVSize,
                    (ref SliceDecoder decoder) => decoder.DecodeSequence<byte>(),
                    useTagEndMarker: false),
                Is.EqualTo(c.H));
        }

        if (c.I != null)
        {
            Assert.That(
                decoder.DecodeTagged(
                    9,
                    TagFormat.VSize,
                    (ref SliceDecoder decoder) => decoder.DecodeSequence<int>(),
                    useTagEndMarker: false),
                Is.EqualTo(c.I));
        }

        if (c.J != null)
        {
            Assert.That(
                decoder.DecodeTagged(
                    10,
                    TagFormat.OVSize,
                    (ref SliceDecoder decoder) => decoder.DecodeString(),
                    useTagEndMarker: false),
                Is.EqualTo(c.J));
        }

        if (hasTaggedMembers)
        {
            Assert.That(decoder.DecodeUInt8(), Is.EqualTo(Slice1Definitions.TagEndMarker));
        }

        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }
}
