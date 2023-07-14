// Copyright (c) ZeroC, Inc.

using NUnit.Framework;
using ZeroC.Slice.Tests.Slice1;
using ZeroC.Slice.Internal;

namespace ZeroC.Slice.Tests;

public class TaggedTests
{
    public static IEnumerable<TestCaseData> EncodeSlice1TaggedFieldsSource
    {
        get
        {
            yield return new TestCaseData(_classWithTaggedFields[0]).SetName(
                "Encode_slice1_tagged_fields(all_fields_set)");

            yield return new TestCaseData(_classWithTaggedFields[1]).SetName(
                "Encode_slice1_tagged_fields(no_fields_set)");

            yield return new TestCaseData(_classWithTaggedFields[2]).SetName(
                "Encode_slice1_tagged_fields(some_fields_set)");
        }
    }

    public static IEnumerable<TestCaseData> EncodeSlice2TaggedFieldsSource
    {
        get
        {
            yield return new TestCaseData(_structWithTaggedFields[0]).SetName(
                "Encode_slice2_tagged_fields(all_fields_set)");

            yield return new TestCaseData(_structWithTaggedFields[1]).SetName(
                "Encode_slice2_tagged_fields(no_fields_set)");

            yield return new TestCaseData(_structWithTaggedFields[2]).SetName(
                "Encode_slice2_tagged_fields(some_fields_set)");
        }
    }

    public static IEnumerable<TestCaseData> DecodeSlice1TaggedFieldsSource
    {
        get
        {
            yield return new TestCaseData(_classWithTaggedFields[0]).SetName(
                "Decode_slice1_tagged_fields(all_fields_set)");

            yield return new TestCaseData(_classWithTaggedFields[1]).SetName(
                "Decode_slice1_tagged_fields(no_fields_set)");

            yield return new TestCaseData(_classWithTaggedFields[2]).SetName(
                "Decode_slice1_tagged_fields(some_fields_set)");
        }
    }

    public static IEnumerable<TestCaseData> SkipSlice1TaggedFieldsSourceWithClassFormat
    {
        get
        {
            yield return new TestCaseData(_classWithTaggedFields[0], ClassFormat.Sliced).SetName(
                "Skip_slice1_tagged_fields(all_fields_set, ClassFormat.Sliced)");

            yield return new TestCaseData(_classWithTaggedFields[0], ClassFormat.Compact).SetName(
                "Skip_slice1_tagged_fields(all_fields_set, ClassFormat.Compact)");

            yield return new TestCaseData(_classWithTaggedFields[1], ClassFormat.Sliced).SetName(
                "Skip_slice1_tagged_fields(no_fields_set, ClassFormat.Sliced)");

            yield return new TestCaseData(_classWithTaggedFields[1], ClassFormat.Compact).SetName(
                "Skip_slice1_tagged_fields(no_fields_set, ClassFormat.Compact)");

            yield return new TestCaseData(_classWithTaggedFields[2], ClassFormat.Sliced).SetName(
                "Skip_slice1_tagged_fields(some_fields_set, ClassFormat.Sliced)");

            yield return new TestCaseData(_classWithTaggedFields[2], ClassFormat.Compact).SetName(
                "Skip_slice1_tagged_fields(some_fields_set, ClassFormat.Compact)");
        }
    }

    public static IEnumerable<TestCaseData> DecodeSlice2TaggedFieldsSource
    {
        get
        {
            yield return new TestCaseData(_structWithTaggedFields[0]).SetName(
                "Decode_slice2_tagged_fields(all_fields_set)");

            yield return new TestCaseData(_structWithTaggedFields[1]).SetName(
                "Decode_slice2_tagged_fields(no_fields_set)");

            yield return new TestCaseData(_structWithTaggedFields[2]).SetName(
                "Decode_slice2_tagged_fields(some_fields_set)");
        }
    }

    public static IEnumerable<TestCaseData> SkipSlice2TaggedFieldsSource
    {
        get
        {
            yield return new TestCaseData(_structWithTaggedFields[0]).SetName(
                "Skip_slice2_tagged_fields(all_fields_set)");

            yield return new TestCaseData(_structWithTaggedFields[1]).SetName(
                "Skip_slice2_tagged_fields(no_fields_set)");

            yield return new TestCaseData(_structWithTaggedFields[2]).SetName(
                "Skip_slice2_tagged_fields(some_fields_set)");
        }
    }

    private static readonly ClassWithTaggedFields[] _classWithTaggedFields = new[]
    {
        new ClassWithTaggedFields(
            10,
            20,
            30,
            40,
            new FixedLengthStruct(1, 1),
            new VarSizeStruct("hello world!"),
            Slice1.MyEnum.Two,
            new byte[] { 1, 2, 3 },
            new int[] { 4, 5, 6 },
            "hello world!"),
        new ClassWithTaggedFields(),
        new ClassWithTaggedFields(
            10,
            null,
            30,
            null,
            new FixedLengthStruct(1, 1),
            null,
            Slice1.MyEnum.Two,
            null,
            new int[] { 4, 5, 6 },
            null)
    };

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

    [Test, TestCaseSource(nameof(DecodeSlice1TaggedFieldsSource))]
    public void Decode_slice1_tagged_fields(ClassWithTaggedFields expected)
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice1);
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
        byte flags = (byte)Slice1Definitions.TypeIdKind.String | (byte)Slice1Definitions.SliceFlags.IsLastSlice;
        if (hasTaggedFields)
        {
            flags |= (byte)Slice1Definitions.SliceFlags.HasTaggedFields;
        }
        encoder.EncodeUInt8(flags);
        encoder.EncodeString(typeof(ClassWithTaggedFields).GetSliceTypeId()!);

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
                (ref SliceEncoder encoder, VarSizeStruct value) => value.Encode(ref encoder));
        }

        if (expected.G is not null)
        {
            encoder.EncodeTagged(
                7,
                TagFormat.Size,
                expected.G.Value,
                (ref SliceEncoder encoder, Slice1.MyEnum value) => encoder.EncodeMyEnum(value));
        }

        if (expected.H is not null)
        {
            encoder.EncodeTagged(
                8,
                TagFormat.OptimizedVSize,
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
                TagFormat.OptimizedVSize,
                expected.J,
                (ref SliceEncoder encoder, string value) => encoder.EncodeString(value));
        }

        if (hasTaggedFields)
        {
            encoder.EncodeUInt8(Slice1Definitions.TagEndMarker);
        }
        var decoder = new SliceDecoder(
            buffer.WrittenMemory,
            SliceEncoding.Slice1,
            activator: IActivator.FromAssembly(typeof(ClassWithTaggedFields).Assembly));

        // Act
        var c = decoder.DecodeClass<ClassWithTaggedFields>();

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

    [Test, TestCaseSource(nameof(DecodeSlice2TaggedFieldsSource))]
    public void Decode_slice2_tagged_fields(MyStructWithTaggedFields expected)
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
        encoder.EncodeVarInt32(Slice2Definitions.TagEndMarker);
        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);

        var decoded = new MyStructWithTaggedFields(ref decoder);
        Assert.That(decoded.A, Is.EqualTo(expected.A));
        Assert.That(decoded.B, Is.EqualTo(expected.B));
        Assert.That(decoded.C, Is.EqualTo(expected.C));
        Assert.That(decoded.D, Is.EqualTo(expected.D));
        Assert.That(decoded.E, Is.EqualTo(expected.E));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test, TestCaseSource(nameof(EncodeSlice1TaggedFieldsSource))]
    public void Encode_slice1_tagged_fields(ClassWithTaggedFields c)
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice1);

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

        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice1);

        Assert.That(decoder.DecodeSize(), Is.EqualTo(1)); // Instance marker
        byte flags = (byte)Slice1Definitions.TypeIdKind.String | (byte)Slice1Definitions.SliceFlags.IsLastSlice;
        if (hasTaggedFields)
        {
            flags |= (byte)Slice1Definitions.SliceFlags.HasTaggedFields;
        }
        Assert.That(decoder.DecodeUInt8(), Is.EqualTo(flags));

        Assert.That(decoder.DecodeString(), Is.EqualTo(typeof(ClassWithTaggedFields).GetSliceTypeId()));

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
                (ref SliceDecoder decoder) => new VarSizeStruct(ref decoder) as VarSizeStruct?,
                useTagEndMarker: false),
            Is.EqualTo(c.F));

        Assert.That(
            decoder.DecodeTagged(
                7,
                TagFormat.Size,
                (ref SliceDecoder decoder) =>
                    Slice1.MyEnumSliceDecoderExtensions.DecodeMyEnum(ref decoder) as Slice1.MyEnum?,
                useTagEndMarker: false),
            Is.EqualTo(c.G));

        Assert.That(
            decoder.DecodeTagged(
                8,
                TagFormat.OptimizedVSize,
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
                TagFormat.OptimizedVSize,
                (ref SliceDecoder decoder) => decoder.DecodeString(),
                useTagEndMarker: false),
            Is.EqualTo(c.J));

        if (hasTaggedFields)
        {
            Assert.That(decoder.DecodeUInt8(), Is.EqualTo(Slice1Definitions.TagEndMarker));
        }

        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test, TestCaseSource(nameof(EncodeSlice2TaggedFieldsSource))]
    public void Encode_slice2_tagged_fields(MyStructWithTaggedFields expected)
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);

        expected.Encode(ref encoder);

        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);

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

        Assert.That(decoder.DecodeVarInt32(), Is.EqualTo(Slice2Definitions.TagEndMarker));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test, TestCaseSource(nameof(SkipSlice1TaggedFieldsSourceWithClassFormat))]
    public void Skip_slice1_tagged_fields(ClassWithTaggedFields expected, ClassFormat classFormat)
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice1, classFormat);
        encoder.EncodeClass(expected);

        // Create an activator that replaces ClassWithTaggedFields by ClassWithoutTaggedFields, both classes
        // are equal except that ClassWithoutTaggedFields doesn't contain any of the tagged fields. Decoding
        // ClassWithTaggedFields as ClassWithoutTaggedFields exercise skipping of tagged values.
        var activator = new TypeReplacementActivator(
            IActivator.FromAssembly(typeof(ClassWithTaggedFields).Assembly),
            typeof(ClassWithTaggedFields).GetSliceTypeId()!,
            typeof(ClassWithoutTaggedFields).GetSliceTypeId()!);

        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice1, activator: activator);

        // Act
        _ = decoder.DecodeClass<ClassWithoutTaggedFields>();

        // Assert
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test, TestCaseSource(nameof(SkipSlice2TaggedFieldsSource))]
    public void Skip_slice2_tagged_fields(MyStructWithTaggedFields value)
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
        value.Encode(ref encoder);

        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice2);

        // Act
        _ = new MyStructWithoutTaggedFields(ref decoder);

        // Assert
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    private class TypeReplacementActivator : IActivator
    {
        private readonly IActivator _decoratee;
        private readonly string _replacementTypeId;
        private readonly string _typeId;

        public object? CreateClassInstance(string typeId, ref SliceDecoder decoder) =>
            _decoratee.CreateClassInstance(typeId == _typeId ? _replacementTypeId : typeId, ref decoder);

        public object? CreateExceptionInstance(string typeId, ref SliceDecoder decoder, string? message) =>
            _decoratee.CreateExceptionInstance(typeId, ref decoder, message);

        internal TypeReplacementActivator(IActivator decoratee, string typeId, string replacementTypeId)
        {
            _decoratee = decoratee;
            _typeId = typeId;
            _replacementTypeId = replacementTypeId;
        }
    }
}
