// Copyright (c) ZeroC, Inc.

using IceRpc.Ice.Codec;
using IceRpc.Ice.Codec.Internal;
using NUnit.Framework;
using ZeroC.Tests.Common;

namespace IceRpc.Ice.Generator.None.Tests;

public class TaggedTests
{
    public static IEnumerable<TestCaseData> EncodeTaggedFieldsSource
    {
        get
        {
            yield return new TestCaseData(_classWithTaggedFields[0]).SetName(
                "Encode_tagged_fields(all_fields_set)");

            yield return new TestCaseData(_classWithTaggedFields[1]).SetName(
                "Encode_tagged_fields(no_fields_set)");

            yield return new TestCaseData(_classWithTaggedFields[2]).SetName(
                "Encode_tagged_fields(some_fields_set)");
        }
    }

    public static IEnumerable<TestCaseData> DecodeTaggedFieldsSource
    {
        get
        {
            yield return new TestCaseData(_classWithTaggedFields[0]).SetName(
                "Decode_tagged_fields(all_fields_set)");

            yield return new TestCaseData(_classWithTaggedFields[1]).SetName(
                "Decode_tagged_fields(no_fields_set)");

            yield return new TestCaseData(_classWithTaggedFields[2]).SetName(
                "Decode_tagged_fields(some_fields_set)");
        }
    }

    public static IEnumerable<TestCaseData> SkipTaggedFieldsSourceWithClassFormat
    {
        get
        {
            yield return new TestCaseData(_classWithTaggedFields[0], ClassFormat.Sliced).SetName(
                "Skip_tagged_fields(all_fields_set, ClassFormat.Sliced)");

            yield return new TestCaseData(_classWithTaggedFields[0], ClassFormat.Compact).SetName(
                "Skip_tagged_fields(all_fields_set, ClassFormat.Compact)");

            yield return new TestCaseData(_classWithTaggedFields[1], ClassFormat.Sliced).SetName(
                "Skip_tagged_fields(no_fields_set, ClassFormat.Sliced)");

            yield return new TestCaseData(_classWithTaggedFields[1], ClassFormat.Compact).SetName(
                "Skip_tagged_fields(no_fields_set, ClassFormat.Compact)");

            yield return new TestCaseData(_classWithTaggedFields[2], ClassFormat.Sliced).SetName(
                "Skip_tagged_fields(some_fields_set, ClassFormat.Sliced)");

            yield return new TestCaseData(_classWithTaggedFields[2], ClassFormat.Compact).SetName(
                "Skip_tagged_fields(some_fields_set, ClassFormat.Compact)");
        }
    }

    private static readonly ClassWithTaggedFields[] _classWithTaggedFields = new[]
    {
        new ClassWithTaggedFields(
            10,
            20,
            30,
            40,
            new FixedSizeStruct(1, 1),
            new VarSizeStruct("hello world!"),
            MyEnum.Two,
            new byte[] { 1, 2, 3 },
            new int[] { 4, 5, 6 },
            "hello world!"),
        new ClassWithTaggedFields(),
        new ClassWithTaggedFields(
            10,
            null,
            30,
            null,
            new FixedSizeStruct(1, 1),
            null,
            MyEnum.Two,
            null,
            new int[] { 4, 5, 6 },
            null)
    };

    [Test, TestCaseSource(nameof(DecodeTaggedFieldsSource))]
    public void Decode_tagged_fields(ClassWithTaggedFields expected)
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new IceEncoder(buffer);
        bool hasTaggedFields =
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
        byte flags = (byte)IceEncodingDefinitions.TypeIdKind.String | (byte)IceEncodingDefinitions.SliceFlags.IsLastSlice;
        if (hasTaggedFields)
        {
            flags |= (byte)IceEncodingDefinitions.SliceFlags.HasTaggedFields;
        }
        encoder.EncodeByte(flags);
        encoder.EncodeString(typeof(ClassWithTaggedFields).GetIceTypeId()!);

        if (expected.A is not null)
        {
            encoder.EncodeTagged(
                1,
                TagFormat.F1,
                expected.A.Value,
                (ref IceEncoder encoder, byte value) => encoder.EncodeByte(value));
        }

        if (expected.B is not null)
        {
            encoder.EncodeTagged(
                2,
                TagFormat.F2,
                expected.B.Value,
                (ref IceEncoder encoder, short value) => encoder.EncodeShort(value));
        }

        if (expected.C is not null)
        {
            encoder.EncodeTagged(
                3,
                TagFormat.F4,
                expected.C.Value,
                (ref IceEncoder encoder, int value) => encoder.EncodeInt(value));
        }

        if (expected.D is not null)
        {
            encoder.EncodeTagged(
                4,
                TagFormat.F8,
                expected.D.Value,
                (ref IceEncoder encoder, long value) => encoder.EncodeLong(value));
        }

        if (expected.E is not null)
        {
            encoder.EncodeTagged(
                5,
                size: 8,
                expected.E.Value,
                (ref IceEncoder encoder, FixedSizeStruct value) => value.Encode(ref encoder));
        }

        if (expected.F is not null)
        {
            encoder.EncodeTagged(
                6,
                TagFormat.FSize,
                expected.F.Value,
                (ref IceEncoder encoder, VarSizeStruct value) => value.Encode(ref encoder));
        }

        if (expected.G is not null)
        {
            encoder.EncodeTagged(
                7,
                TagFormat.Size,
                expected.G.Value,
                (ref IceEncoder encoder, MyEnum value) => encoder.EncodeMyEnum(value));
        }

        if (expected.H is not null)
        {
            encoder.EncodeTagged(
                8,
                TagFormat.OptimizedVSize,
                expected.H,
                (ref IceEncoder encoder, IList<byte> value) => encoder.EncodeSequence(value));
        }

        if (expected.I is not null)
        {
            encoder.EncodeTagged(
                9,
                size: IceEncoder.GetSizeLength(expected.I.Count) + (4 * expected.I.Count),
                expected.I,
                (ref IceEncoder encoder, IList<int> value) => encoder.EncodeSequence(value));
        }

        if (expected.J is not null)
        {
            encoder.EncodeTagged(
                10,
                TagFormat.OptimizedVSize,
                expected.J,
                (ref IceEncoder encoder, string value) => encoder.EncodeString(value));
        }

        if (hasTaggedFields)
        {
            encoder.EncodeByte(IceEncodingDefinitions.TagEndMarker);
        }
        var decoder = new IceDecoder(
            buffer.WrittenMemory,
            activator: IActivator.FromAssembly(typeof(ClassWithTaggedFields).Assembly));

        // Act
        var c = decoder.DecodeClass<ClassWithTaggedFields>()!;

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

    [Test, TestCaseSource(nameof(EncodeTaggedFieldsSource))]
    public void Encode_tagged_fields(ClassWithTaggedFields c)
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new IceEncoder(buffer);

        // Act
        encoder.EncodeClass(c);

        // Assert
        bool hasTaggedFields =
            c.A is not null ||
            c.B is not null ||
            c.D is not null ||
            c.E is not null ||
            c.F is not null ||
            c.G is not null ||
            c.H is not null ||
            c.I is not null ||
            c.J is not null;

        var decoder = new IceDecoder(buffer.WrittenMemory);

        Assert.That(decoder.DecodeSize(), Is.EqualTo(1)); // Instance marker
        byte flags = (byte)IceEncodingDefinitions.TypeIdKind.String | (byte)IceEncodingDefinitions.SliceFlags.IsLastSlice;
        if (hasTaggedFields)
        {
            flags |= (byte)IceEncodingDefinitions.SliceFlags.HasTaggedFields;
        }
        Assert.That(decoder.DecodeByte(), Is.EqualTo(flags));

        Assert.That(decoder.DecodeString(), Is.EqualTo(typeof(ClassWithTaggedFields).GetIceTypeId()));

        Assert.That(
            decoder.DecodeTagged(
                1,
                TagFormat.F1,
                (ref IceDecoder decoder) => decoder.DecodeByte() as byte?,
                useTagEndMarker: false),
            Is.EqualTo(c.A));

        Assert.That(
            decoder.DecodeTagged(
                2,
                TagFormat.F2,
                (ref IceDecoder decoder) => decoder.DecodeShort() as short?,
                useTagEndMarker: false),
            Is.EqualTo(c.B));

        Assert.That(
            decoder.DecodeTagged(
                3,
                TagFormat.F4,
                (ref IceDecoder decoder) => decoder.DecodeInt() as int?,
                useTagEndMarker: false),
            Is.EqualTo(c.C));

        Assert.That(
            decoder.DecodeTagged(
                4,
                TagFormat.F8,
                (ref IceDecoder decoder) => decoder.DecodeLong() as long?,
                useTagEndMarker: false),
            Is.EqualTo(c.D));

        Assert.That(
            decoder.DecodeTagged(
                5,
                TagFormat.VSize,
                (ref IceDecoder decoder) => new FixedSizeStruct(ref decoder) as FixedSizeStruct?,
                useTagEndMarker: false),
            Is.EqualTo(c.E));

        Assert.That(
            decoder.DecodeTagged(
                6,
                TagFormat.FSize,
                (ref IceDecoder decoder) => new VarSizeStruct(ref decoder) as VarSizeStruct?,
                useTagEndMarker: false),
            Is.EqualTo(c.F));

        Assert.That(
            decoder.DecodeTagged(
                7,
                TagFormat.Size,
                (ref IceDecoder decoder) =>
                    MyEnumIceDecoderExtensions.DecodeMyEnum(ref decoder) as MyEnum?,
                useTagEndMarker: false),
            Is.EqualTo(c.G));

        Assert.That(
            decoder.DecodeTagged(
                8,
                TagFormat.OptimizedVSize,
                (ref IceDecoder decoder) => decoder.DecodeSequence<byte>(),
                useTagEndMarker: false),
            Is.EqualTo(c.H));

        Assert.That(
            decoder.DecodeTagged(
                9,
                TagFormat.VSize,
                (ref IceDecoder decoder) => decoder.DecodeSequence<int>(),
                useTagEndMarker: false),
            Is.EqualTo(c.I));

        Assert.That(
            decoder.DecodeTagged(
                10,
                TagFormat.OptimizedVSize,
                (ref IceDecoder decoder) => decoder.DecodeString(),
                useTagEndMarker: false),
            Is.EqualTo(c.J));

        if (hasTaggedFields)
        {
            Assert.That(decoder.DecodeByte(), Is.EqualTo(IceEncodingDefinitions.TagEndMarker));
        }

        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test, TestCaseSource(nameof(SkipTaggedFieldsSourceWithClassFormat))]
    public void Skip_tagged_fields(ClassWithTaggedFields expected, ClassFormat classFormat)
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new IceEncoder(buffer, classFormat);
        encoder.EncodeClass(expected);

        // Create an activator that replaces ClassWithTaggedFields by ClassWithoutTaggedFields, both classes
        // are equal except that ClassWithoutTaggedFields doesn't contain any of the tagged fields. Decoding
        // ClassWithTaggedFields as ClassWithoutTaggedFields exercise skipping of tagged values.
        var activator = new TypeReplacementActivator(
            IActivator.FromAssembly(typeof(ClassWithTaggedFields).Assembly),
            typeof(ClassWithTaggedFields).GetIceTypeId()!,
            typeof(ClassWithoutTaggedFields).GetIceTypeId()!);

        var decoder = new IceDecoder(buffer.WrittenMemory, activator: activator);

        // Act
        _ = decoder.DecodeClass<ClassWithoutTaggedFields>();

        // Assert
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    private class TypeReplacementActivator : IActivator
    {
        private readonly IActivator _decoratee;
        private readonly string _replacementTypeId;
        private readonly string _typeId;

        public object? CreateInstance(string typeId) =>
            _decoratee.CreateInstance(typeId == _typeId ? _replacementTypeId : typeId);

        internal TypeReplacementActivator(IActivator decoratee, string typeId, string replacementTypeId)
        {
            _decoratee = decoratee;
            _typeId = typeId;
            _replacementTypeId = replacementTypeId;
        }
    }
}
