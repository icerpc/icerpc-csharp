// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Slice.Internal;
using IceRpc.Tests.Slice.Tagged.Slice1;
using NUnit.Framework;

namespace IceRpc.Tests.Slice;

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

    public static IEnumerable<TestCaseData> EncodeSlice2TagggedMembersSource
    {
        get
        {
            yield return new TestCaseData(_structWithTaggedMembers[0]).SetName(
                "Encode_slice2_tagged_members(all_members_set)");

            yield return new TestCaseData(_structWithTaggedMembers[1]).SetName(
                "Encode_slice2_tagged_members(no_members_set)");

            yield return new TestCaseData(_structWithTaggedMembers[2]).SetName(
                "Encode_slice2_tagged_members(some_members_set)");
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

    public static IEnumerable<TestCaseData> DecodeSlice2TagggedMembersSource
    {
        get
        {
            yield return new TestCaseData(_structWithTaggedMembers[0]).SetName(
                "Decode_slice2_tagged_members(all_members_set)");

            yield return new TestCaseData(_structWithTaggedMembers[1]).SetName(
                "Decode_slice2_tagged_members(no_members_set)");

            yield return new TestCaseData(_structWithTaggedMembers[2]).SetName(
                "Decode_slice2_tagged_members(some_members_set)");
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
                    Tagged.Slice1.MyEnum.Two,
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
                    Tagged.Slice1.MyEnum.Two,
                    null,
                    new int[] { 4, 5, 6 },
                    null)
    };

    private static readonly MyStructWithTaggedMembers[] _structWithTaggedMembers = new MyStructWithTaggedMembers[]
    {
        new MyStructWithTaggedMembers(10,
                                      new MyStruct(20, 20),
                                      MyEnum.enum1,
                                      new byte[] { 1, 2, 3},
                                      "hello world!",
                                      new TraitStructA("hello world!")),
        new MyStructWithTaggedMembers(),
        new MyStructWithTaggedMembers(10,
                                      null,
                                      MyEnum.enum1,
                                      null,
                                      "hello world!",
                                      null),
    };

    [Test, TestCaseSource(nameof(DecodeSlice1TagggedMembersSource))]
    public void Decode_slice1_tagged_members(ClassWithTaggedMembers expected)
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice1);
        bool hasTaggedMembers =
            expected.A is not null ||
            expected.B is not null ||
            expected.D is not null ||
            expected.E is not null ||
            expected.F is not null ||
            expected.G is not null ||
            expected.H is not null ||
            expected.I is not null ||
            expected.J is not null;

        encoder.EncodeSize(1); // Instance marker
        byte flags = (byte)Slice1Definitions.TypeIdKind.String | (byte)Slice1Definitions.SliceFlags.IsLastSlice;
        if (hasTaggedMembers)
        {
            flags |= (byte)Slice1Definitions.SliceFlags.HasTaggedMembers;
        }
        encoder.EncodeUInt8(flags);
        encoder.EncodeString(ClassWithTaggedMembers.SliceTypeId);

        if (expected.A is not null)
        {
            encoder.EncodeTagged(
                1,
                TagFormat.F1,
                expected.A.Value,
                (ref SliceEncoder encoder, byte value) => encoder.EncodeUInt8(value));
        }

        if (expected.B is not null)
        {
            encoder.EncodeTagged(
                2,
                TagFormat.F2,
                expected.B.Value,
                (ref SliceEncoder encoder, short value) => encoder.EncodeInt16(value));
        }

        if (expected.C is not null)
        {
            encoder.EncodeTagged(
                3,
                TagFormat.F4,
                expected.C.Value,
                (ref SliceEncoder encoder, int value) => encoder.EncodeInt32(value));
        }

        if (expected.D is not null)
        {
            encoder.EncodeTagged(
                4,
                TagFormat.F8,
                expected.D.Value,
                (ref SliceEncoder encoder, long value) => encoder.EncodeInt64(value));
        }

        if (expected.E is not null)
        {
            encoder.EncodeTagged(
                5,
                size: 8,
                expected.E.Value,
                (ref SliceEncoder encoder, FixedLengthStruct value) => value.Encode(ref encoder));
        }

        if (expected.F is not null)
        {
            encoder.EncodeTagged(
                6,
                TagFormat.FSize,
                expected.F.Value,
                (ref SliceEncoder encoder, VarLengthStruct value) => value.Encode(ref encoder));
        }

        if (expected.G is not null)
        {
            encoder.EncodeTagged(
                7,
                TagFormat.Size,
                expected.G.Value,
                (ref SliceEncoder encoder, Tagged.Slice1.MyEnum value) => encoder.EncodeMyEnum(value));
        }

        if (expected.H is not null)
        {
            encoder.EncodeTagged(
                8,
                TagFormat.OVSize,
                expected.H,
                (ref SliceEncoder encoder, IList<byte> value) => encoder.EncodeSequence(value));
        }

        if (expected.I is not null)
        {
            encoder.EncodeTagged(
                9,
                size: encoder.GetSizeLength(expected.I.Count) + (4 * expected.I.Count),
                expected.I,
                (ref SliceEncoder encoder, IList<int> value) => encoder.EncodeSequence(value));
        }

        if (expected.J is not null)
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

    [Test, TestCaseSource(nameof(DecodeSlice2TagggedMembersSource))]
    public void Decode_slice2_tagged_members(MyStructWithTaggedMembers expected)
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
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
        if (expected.F is IMyTraitA f)
        {
            encoder.EncodeTagged(6, f, (ref SliceEncoder encoder, IMyTraitA value) => f.EncodeTrait(ref encoder));
        }
        encoder.EncodeVarInt32(Slice2Definitions.TagEndMarker);
        var decoder = new SliceDecoder(
            buffer.WrittenMemory,
            SliceEncoding.Slice2,
            activator: SliceDecoder.GetActivator(typeof(TraitStructA).Assembly));

        var decoded = new MyStructWithTaggedMembers(ref decoder);
        Assert.That(decoded.A, Is.EqualTo(expected.A));
        Assert.That(decoded.B, Is.EqualTo(expected.B));
        Assert.That(decoded.C, Is.EqualTo(expected.C));
        Assert.That(decoded.D, Is.EqualTo(expected.D));
        Assert.That(decoded.E, Is.EqualTo(expected.E));
        Assert.That(decoded.F, Is.EqualTo(expected.F));
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
            c.A is not null ||
            c.B is not null ||
            c.D is not null ||
            c.E is not null ||
            c.F is not null ||
            c.G is not null ||
            c.H is not null ||
            c.I is not null ||
            c.J is not null;

        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice1);

        Assert.That(decoder.DecodeSize(), Is.EqualTo(1)); // Instance marker
        byte flags = (byte)Slice1Definitions.TypeIdKind.String | (byte)Slice1Definitions.SliceFlags.IsLastSlice;
        if (hasTaggedMembers)
        {
            flags |= (byte)Slice1Definitions.SliceFlags.HasTaggedMembers;
        }
        Assert.That(decoder.DecodeUInt8(), Is.EqualTo(flags));

        Assert.That(decoder.DecodeString(), Is.EqualTo(ClassWithTaggedMembers.SliceTypeId));

        Assert.That(
            decoder.DecodeTagged(
                1,
                TagFormat.F1,
                (ref SliceDecoder decoder) => decoder.DecodeUInt8() as byte?,
                useTagEndMarker: false),
            Is.EqualTo(c.A));

        Assert.That(
            decoder.DecodeTagged(
                2,
                TagFormat.F2,
                (ref SliceDecoder decoder) => decoder.DecodeInt16() as short?,
                useTagEndMarker: false),
            Is.EqualTo(c.B));

        Assert.That(
            decoder.DecodeTagged(
                3,
                TagFormat.F4,
                (ref SliceDecoder decoder) => decoder.DecodeInt32() as int?,
                useTagEndMarker: false),
            Is.EqualTo(c.C));

        Assert.That(
            decoder.DecodeTagged(
                4,
                TagFormat.F8,
                (ref SliceDecoder decoder) => decoder.DecodeInt64() as long?,
                useTagEndMarker: false),
            Is.EqualTo(c.D));

        Assert.That(
            decoder.DecodeTagged(
                5,
                TagFormat.VSize,
                (ref SliceDecoder decoder) => new FixedLengthStruct(ref decoder) as FixedLengthStruct?,
                useTagEndMarker: false),
            Is.EqualTo(c.E));

        Assert.That(
            decoder.DecodeTagged(
                6,
                TagFormat.FSize,
                (ref SliceDecoder decoder) => new VarLengthStruct(ref decoder) as VarLengthStruct?,
                useTagEndMarker: false),
            Is.EqualTo(c.F));

        Assert.That(
            decoder.DecodeTagged(
                7,
                TagFormat.Size,
                (ref SliceDecoder decoder) =>
                    Tagged.Slice1.MyEnumSliceDecoderExtensions.DecodeMyEnum(ref decoder) as Tagged.Slice1.MyEnum?,
                useTagEndMarker: false),
            Is.EqualTo(c.G));

        Assert.That(
            decoder.DecodeTagged(
                8,
                TagFormat.OVSize,
                (ref SliceDecoder decoder) => decoder.DecodeSequence<byte>(),
                useTagEndMarker: false),
            Is.EqualTo(c.H));

        Assert.That(
            decoder.DecodeTagged(
                9,
                TagFormat.VSize,
                (ref SliceDecoder decoder) => decoder.DecodeSequence<int>(),
                useTagEndMarker: false),
            Is.EqualTo(c.I));

        Assert.That(
            decoder.DecodeTagged(
                10,
                TagFormat.OVSize,
                (ref SliceDecoder decoder) => decoder.DecodeString(),
                useTagEndMarker: false),
            Is.EqualTo(c.J));

        if (hasTaggedMembers)
        {
            Assert.That(decoder.DecodeUInt8(), Is.EqualTo(Slice1Definitions.TagEndMarker));
        }

        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test, TestCaseSource(nameof(EncodeSlice2TagggedMembersSource))]
    public void Encode_slice2_tagged_members(MyStructWithTaggedMembers expected)
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);

        expected.Encode(ref encoder);

        var decoder = new SliceDecoder(
            buffer.WrittenMemory,
            SliceEncoding.Slice2,
            activator: SliceDecoder.GetActivator(typeof(TraitStructA).Assembly));
        Assert.That(
            decoder.DecodeTagged(
                1,
                (ref SliceDecoder decoder) => decoder.DecodeUInt8() as byte?,
                useTagEndMarker: true),
            Is.EqualTo(expected.A));

        Assert.That(
            decoder.DecodeTagged(
                2,
                (ref SliceDecoder decoder) => new MyStruct(ref decoder) as MyStruct?,
                useTagEndMarker: true),
            Is.EqualTo(expected.B));

        Assert.That(
            decoder.DecodeTagged(
                3,
                (ref SliceDecoder decoder) => decoder.DecodeMyEnum() as MyEnum?,
                useTagEndMarker: true),
            Is.EqualTo(expected.C));

        Assert.That(
            decoder.DecodeTagged(
                4,
                (ref SliceDecoder decoder) => decoder.DecodeSequence<byte>(),
                useTagEndMarker: true),
            Is.EqualTo(expected.D));

        Assert.That(
            decoder.DecodeTagged(
                5,
                (ref SliceDecoder decoder) => decoder.DecodeString(),
                useTagEndMarker: true),
            Is.EqualTo(expected.E));

        Assert.That(
            decoder.DecodeTagged(
                6,
                (ref SliceDecoder decoder) => decoder.DecodeTrait<IMyTraitA?>(),
                useTagEndMarker: true),
            Is.EqualTo(expected.F));
        Assert.That(decoder.DecodeVarInt32(), Is.EqualTo(Slice2Definitions.TagEndMarker));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }
}
