// Copyright (c) ZeroC, Inc.

using IceRpc.Ice.Codec;
using IceRpc.Ice.Codec.Internal;
using NUnit.Framework;
using ZeroC.Tests.Common;

namespace IceRpc.Ice.Generator.None.Tests;

[Parallelizable(ParallelScope.All)]
public sealed class ClassTests
{
    [Test]
    public void Class_graph_max_depth()
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[1024 * 1024]);
        var encoder = new IceEncoder(buffer);
        var theA = new MyClassA();
        var theB = new MyClassB();
        var theC = new MyClassC();

        theA.TheB = theB;
        theA.TheC = theC;
        for (int i = 0; i < 100; i++)
        {
            theC = new MyClassC();
            theC.TheB = new MyClassB();
            theB!.TheC = theC;
            theB = theB.TheC.TheB;
        }
        encoder.EncodeClass(theA);

        // Act/Assert
        Assert.That(
            () =>
            {
                var decoder = new IceDecoder(
                    buffer.WrittenMemory,
                    activator: IActivator.FromAssembly(typeof(MyClassA).Assembly),
                    maxDepth: 100);
                decoder.DecodeClass<MyClassA>();
            },
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Encode_and_decode_internal_class()
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[1024 * 1024]);
        var encoder = new IceEncoder(buffer);
        encoder.EncodeClass(new MyInternalClass("m1", "m2"));

        var decoder = new IceDecoder(
                    buffer.WrittenMemory,
                    activator: IActivator.FromAssemblies(typeof(MyInternalClass).Assembly),
                    maxDepth: 100);
        // Act
        MyInternalClass? decoded = decoder.DecodeClass<MyInternalClass>();

        Assert.That(decoded?.M1, Is.EqualTo("m1"));
        Assert.That(decoded?.M2, Is.EqualTo("m2"));
    }

    [Test]
    public void Encode_class_with_compact_format()
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new IceEncoder(buffer);

        // Act
        encoder.EncodeClass(
            new MyClassA
            {
                TheB = new MyClassB(),
                TheC = new MyClassC(),
            });

        // Assert
        var decoder = new IceDecoder(buffer.WrittenMemory);

        Assert.That(decoder.DecodeSize(), Is.EqualTo(1)); // Instance marker
        Assert.That(
            decoder.DecodeUInt8(),
            Is.EqualTo(
                (byte)IceEncodingDefinitions.TypeIdKind.String |  // The first Slice include a type Id
                (byte)IceEncodingDefinitions.SliceFlags.IsLastSlice));
        Assert.That(decoder.DecodeString(), Is.EqualTo(typeof(MyClassA).GetIceTypeId()));

        // MyClassA.theB field encoded inline (2 Slices)

        Assert.That(decoder.DecodeSize(), Is.EqualTo(1)); // Instance marker

        // theB - First Slice
        Assert.That(decoder.DecodeUInt8(), Is.EqualTo((byte)IceEncodingDefinitions.TypeIdKind.String));
        Assert.That(decoder.DecodeString(), Is.EqualTo(typeof(MyClassB).GetIceTypeId()));
        Assert.That(decoder.DecodeSize(), Is.EqualTo(0)); // null instance

        // theB - Second Slice
        Assert.That(
            decoder.DecodeUInt8(),
            Is.EqualTo((byte)IceEncodingDefinitions.SliceFlags.IsLastSlice));
        Assert.That(decoder.DecodeSize(), Is.EqualTo(0)); // null instance
        Assert.That(decoder.DecodeSize(), Is.EqualTo(0)); // null instance

        // MyClassA.theC field encoded inline (1 Slice)

        Assert.That(decoder.DecodeUInt8(), Is.EqualTo(1));

        // theC - First Slice
        Assert.That(
            decoder.DecodeUInt8(),
            Is.EqualTo((byte)IceEncodingDefinitions.TypeIdKind.String | (byte)IceEncodingDefinitions.SliceFlags.IsLastSlice));
        Assert.That(decoder.DecodeString(), Is.EqualTo(typeof(MyClassC).GetIceTypeId()));
        Assert.That(decoder.DecodeSize(), Is.EqualTo(0)); // null instance

        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Encode_class_with_sliced_format()
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new IceEncoder(buffer, classFormat: ClassFormat.Sliced);

        // Act
        encoder.EncodeClass(new MyClassA
        {
            TheB = new MyClassB(),
            TheC = new MyClassC(),
        });

        // Assert
        var decoder = new IceDecoder(buffer.WrittenMemory);

        Assert.That(decoder.DecodeSize(), Is.EqualTo(1)); // Instance marker

        Assert.That(
            decoder.DecodeUInt8(),
            Is.EqualTo(
                (byte)IceEncodingDefinitions.TypeIdKind.String |
                (byte)IceEncodingDefinitions.SliceFlags.HasIndirectionTable | // The sliced format includes an indirection
                (byte)IceEncodingDefinitions.SliceFlags.HasSliceSize |        // table and the Slice size for MyClassA
                (byte)IceEncodingDefinitions.SliceFlags.IsLastSlice));

        Assert.That(decoder.DecodeString(), Is.EqualTo(typeof(MyClassA).GetIceTypeId()));

        Assert.That(decoder.DecodeInt32(), Is.EqualTo(6)); // Slice size (int size + two references)
        Assert.That(decoder.DecodeSize(), Is.EqualTo(1));  // Reference the first entry in the indirection table
        Assert.That(decoder.DecodeSize(), Is.EqualTo(2));  // Reference the second entry in the indirection table

        Assert.That(decoder.DecodeSize(), Is.EqualTo(2));  // Size of the indirection table

        // MyClassA.theB field encoded in the indirection table (2 Slices)

        Assert.That(decoder.DecodeSize(), Is.EqualTo(1)); // Instance marker

        // theB - First Slice
        Assert.That(
            decoder.DecodeUInt8(),
            Is.EqualTo(
                (byte)IceEncodingDefinitions.TypeIdKind.String |
                (byte)IceEncodingDefinitions.SliceFlags.HasSliceSize));
        Assert.That(decoder.DecodeString(), Is.EqualTo(typeof(MyClassB).GetIceTypeId()));
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(5)); // Slice size (int size + one reference)
        Assert.That(decoder.DecodeSize(), Is.EqualTo(0)); // null instance

        // theB - Second Slice
        Assert.That(
            decoder.DecodeUInt8(),
            Is.EqualTo(
                (byte)IceEncodingDefinitions.TypeIdKind.Index |
                (byte)IceEncodingDefinitions.SliceFlags.HasSliceSize |
                (byte)IceEncodingDefinitions.SliceFlags.IsLastSlice));
        Assert.That(decoder.DecodeSize(), Is.EqualTo(1)); // TypeId encoded as an index as this TypeId already appears
                                                          // with the first instance.
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(6)); // Slice size (int size + two references)
        Assert.That(decoder.DecodeSize(), Is.EqualTo(0)); // null instance
        Assert.That(decoder.DecodeSize(), Is.EqualTo(0)); // null instance

        // MyClassA.theC field encoded in the indirection table (1 Slices)

        Assert.That(decoder.DecodeSize(), Is.EqualTo(1)); // Instance marker

        // theC - First Slice
        Assert.That(
            decoder.DecodeUInt8(),
            Is.EqualTo(
                (byte)IceEncodingDefinitions.TypeIdKind.String |
                (byte)IceEncodingDefinitions.SliceFlags.HasSliceSize |
                (byte)IceEncodingDefinitions.SliceFlags.IsLastSlice));
        Assert.That(decoder.DecodeString(), Is.EqualTo(typeof(MyClassC).GetIceTypeId()));
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(5)); // Slice size (int size + one reference)
        Assert.That(decoder.DecodeSize(), Is.EqualTo(0)); // null instance

        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Encode_class_graph_with_compact_format()
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new IceEncoder(buffer);
        var theA = new MyClassA();
        var theB = new MyClassB();
        var theC = new MyClassC();

        theA.TheB = theB;
        theA.TheC = theC;

        theB.TheC = theC;

        theC.TheB = theB;

        // Act
        encoder.EncodeClass(theA);

        // Assert

        // The class graph is encoded inline when using the compact format
        // theA index 2
        // theB index 3
        // theC index 4

        var decoder = new IceDecoder(buffer.WrittenMemory);

        Assert.That(decoder.DecodeSize(), Is.EqualTo(1)); // Instance marker

        Assert.That(
            decoder.DecodeUInt8(),
            Is.EqualTo((byte)IceEncodingDefinitions.TypeIdKind.String | (byte)IceEncodingDefinitions.SliceFlags.IsLastSlice));
        Assert.That(decoder.DecodeString(), Is.EqualTo(typeof(MyClassA).GetIceTypeId()));

        // MyClassA.theB field encoded inline (2 Slices)

        Assert.That(decoder.DecodeSize(), Is.EqualTo(1)); // Instance marker

        // theB - First Slice
        Assert.That(decoder.DecodeUInt8(), Is.EqualTo((byte)IceEncodingDefinitions.TypeIdKind.String));
        Assert.That(decoder.DecodeString(), Is.EqualTo(typeof(MyClassB).GetIceTypeId()));
        Assert.That(decoder.DecodeSize(), Is.EqualTo(0)); // null reference

        // theB - Second Slice
        Assert.That(decoder.DecodeUInt8(), Is.EqualTo((byte)IceEncodingDefinitions.SliceFlags.IsLastSlice));
        Assert.That(decoder.DecodeSize(), Is.EqualTo(0)); // null reference

        // theB.theC instance encoded inline
        Assert.That(decoder.DecodeSize(), Is.EqualTo(1)); // Instance marker

        // MyClassA.theB.theC encoded inline (1 Slice)
        Assert.That(
            decoder.DecodeUInt8(),
            Is.EqualTo((byte)IceEncodingDefinitions.TypeIdKind.String | (byte)IceEncodingDefinitions.SliceFlags.IsLastSlice));
        Assert.That(decoder.DecodeString(), Is.EqualTo(typeof(MyClassC).GetIceTypeId()));
        Assert.That(decoder.DecodeSize(), Is.EqualTo(3)); // reference to the 3rd instance

        // MyClassA.TheC encoded as an index
        Assert.That(decoder.DecodeSize(), Is.EqualTo(4)); // reference to the 4th instance

        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Encode_class_graph_with_sliced_format()
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new IceEncoder(buffer, classFormat: ClassFormat.Sliced);
        var theA = new MyClassA();
        var theB = new MyClassB();
        var theC = new MyClassC();

        theA.TheB = theB;
        theA.TheC = theC;

        theB.TheC = theC;

        theC.TheB = theB;

        // Act
        encoder.EncodeClass(theA);

        // Assert

        var decoder = new IceDecoder(buffer.WrittenMemory);

        Assert.That(decoder.DecodeSize(), Is.EqualTo(1)); // Instance marker
        Assert.That(
            decoder.DecodeUInt8(),
            Is.EqualTo(
                (byte)IceEncodingDefinitions.TypeIdKind.String |
                (byte)IceEncodingDefinitions.SliceFlags.HasIndirectionTable |
                (byte)IceEncodingDefinitions.SliceFlags.HasSliceSize |
                (byte)IceEncodingDefinitions.SliceFlags.IsLastSlice));
        Assert.That(decoder.DecodeString(), Is.EqualTo(typeof(MyClassA).GetIceTypeId()));
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(6)); // Slice size

        Assert.That(decoder.DecodeSize(), Is.EqualTo(1)); // (reference 1st entry of the indirection table)
        Assert.That(decoder.DecodeSize(), Is.EqualTo(2)); // (reference 2nd entry of the indirection table)

        Assert.That(decoder.DecodeSize(), Is.EqualTo(2)); // Indirection table size

        // MyClassA.theB field encoded in the indirection table (2 Slices)
        Assert.That(decoder.DecodeSize(), Is.EqualTo(1)); // Instance marker
        // First Slice
        Assert.That(
            decoder.DecodeUInt8(),
            Is.EqualTo(
                (byte)IceEncodingDefinitions.TypeIdKind.String |
                (byte)IceEncodingDefinitions.SliceFlags.HasSliceSize));

        // theB - First Slice
        Assert.That(decoder.DecodeString(), Is.EqualTo(typeof(MyClassB).GetIceTypeId()));
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(5)); // Slice size (int size + one reference)
        Assert.That(decoder.DecodeSize(), Is.EqualTo(0)); // null instance

        // theB - Second Slice
        Assert.That(
            decoder.DecodeUInt8(),
            Is.EqualTo(
                (byte)IceEncodingDefinitions.TypeIdKind.Index |
                (byte)IceEncodingDefinitions.SliceFlags.HasIndirectionTable |
                (byte)IceEncodingDefinitions.SliceFlags.HasSliceSize |
                (byte)IceEncodingDefinitions.SliceFlags.IsLastSlice));
        Assert.That(decoder.DecodeSize(), Is.EqualTo(1)); // TypeId encoded as an index as this TypeId already appears
                                                          // with the first instance.
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(6)); // Slice size (int size + two references)
        Assert.That(decoder.DecodeSize(), Is.EqualTo(0)); // null instance
        Assert.That(decoder.DecodeSize(), Is.EqualTo(1)); // (reference 1st entry of the indirection table)

        Assert.That(decoder.DecodeSize(), Is.EqualTo(1)); // Indirection table size

        // MyClassA.theB.theC field encoded in the indirection table (1 Slice)
        Assert.That(decoder.DecodeSize(), Is.EqualTo(1)); // Instance marker
        Assert.That(
            decoder.DecodeUInt8(),
            Is.EqualTo(
                (byte)IceEncodingDefinitions.TypeIdKind.String |
                (byte)IceEncodingDefinitions.SliceFlags.HasIndirectionTable |
                (byte)IceEncodingDefinitions.SliceFlags.HasSliceSize |
                (byte)IceEncodingDefinitions.SliceFlags.IsLastSlice));

        // First Slice
        Assert.That(decoder.DecodeString(), Is.EqualTo(typeof(MyClassC).GetIceTypeId()));
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(5)); // Slice size (int size + one reference)
        Assert.That(decoder.DecodeSize(), Is.EqualTo(1)); // reference 1st entry of the indirection table

        Assert.That(decoder.DecodeSize(), Is.EqualTo(1)); // Indirection table size

        Assert.That(decoder.DecodeSize(), Is.EqualTo(3)); // Reference to index 3

        // MyClassA.theC encoded as an index
        Assert.That(decoder.DecodeSize(), Is.EqualTo(4)); // Reference to index 4

        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Encode_class_with_compact_id_and_compact_format()
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new IceEncoder(buffer);

        // Act
        encoder.EncodeClass(new MyDerivedCompactClass());

        // Assert
        var decoder = new IceDecoder(buffer.WrittenMemory);

        Assert.That(decoder.DecodeSize(), Is.EqualTo(1)); // Instance marker
        Assert.That(decoder.DecodeUInt8(), Is.EqualTo((byte)IceEncodingDefinitions.TypeIdKind.CompactId));
        Assert.That(decoder.DecodeSize(), Is.EqualTo(typeof(MyDerivedCompactClass).GetCompactIceTypeId()!.Value));

        Assert.That(decoder.DecodeUInt8(), Is.EqualTo((byte)IceEncodingDefinitions.SliceFlags.IsLastSlice));

        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Encode_class_with_compact_id_and_sliced_format()
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new IceEncoder(buffer, classFormat: ClassFormat.Sliced);

        // Act
        encoder.EncodeClass(new MyDerivedCompactClass());

        // Assert
        var decoder = new IceDecoder(
            buffer.WrittenMemory,
            activator: IActivator.FromAssembly(typeof(MyDerivedCompactClass).Assembly));

        Assert.That(decoder.DecodeSize(), Is.EqualTo(1)); // Instance marker
        Assert.That(
            decoder.DecodeUInt8(),
            Is.EqualTo(
                (byte)IceEncodingDefinitions.TypeIdKind.CompactId |
                (byte)IceEncodingDefinitions.SliceFlags.HasSliceSize));
        Assert.That(decoder.DecodeSize(), Is.EqualTo(typeof(MyDerivedCompactClass).GetCompactIceTypeId()!.Value));
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(4)); // Empty Slice 4 bytes

        Assert.That(
            decoder.DecodeUInt8(),
            Is.EqualTo(
                (byte)IceEncodingDefinitions.TypeIdKind.CompactId |

                (byte)IceEncodingDefinitions.SliceFlags.HasSliceSize |
                (byte)IceEncodingDefinitions.SliceFlags.IsLastSlice));
        Assert.That(decoder.DecodeSize(), Is.EqualTo(typeof(MyCompactClass).GetCompactIceTypeId()!.Value));
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(4)); // Empty Slice 4 bytes

        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Encode_class_with_tagged_fields_and_compact_format(
        [Values(10, null)] int? a,
        [Values("hello world!", null)] string? b)
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new IceEncoder(buffer);

        // Act
        encoder.EncodeClass(new MyDerivedClassWithTaggedFields(a, b));

        // Assert
        var decoder = new IceDecoder(buffer.WrittenMemory);

        Assert.That(decoder.DecodeSize(), Is.EqualTo(1)); // Instance marker
        Assert.That(decoder.DecodeUInt8(),
            b is null ?
                Is.EqualTo((byte)IceEncodingDefinitions.TypeIdKind.String) :
                Is.EqualTo(
                    (byte)IceEncodingDefinitions.TypeIdKind.String |
                    (byte)IceEncodingDefinitions.SliceFlags.HasTaggedFields));

        Assert.That(decoder.DecodeString(), Is.EqualTo(typeof(MyDerivedClassWithTaggedFields).GetIceTypeId()));

        // MyDerivedClassWithTaggedFields.B
        if (b is not null)
        {
            Assert.That(
                decoder.DecodeTagged(
                    20,
                    TagFormat.OptimizedVSize,
                    (ref IceDecoder decoder) => decoder.DecodeString(),
                    useTagEndMarker: false),
                Is.EqualTo(b));
            Assert.That(decoder.DecodeUInt8(), Is.EqualTo(IceEncodingDefinitions.TagEndMarker));
        }

        Assert.That(decoder.DecodeUInt8(),
            a is null ?
                Is.EqualTo((byte)IceEncodingDefinitions.SliceFlags.IsLastSlice) :
                Is.EqualTo(
                    (byte)IceEncodingDefinitions.SliceFlags.HasTaggedFields |
                    (byte)IceEncodingDefinitions.SliceFlags.IsLastSlice));
        // MyClassWithTaggedFields.A
        if (a is not null)
        {
            Assert.That(
                decoder.DecodeTagged(
                    10,
                    TagFormat.F4,
                    (ref IceDecoder decoder) => decoder.DecodeInt32(),
                    useTagEndMarker: false),
                Is.EqualTo(a));
            Assert.That(decoder.DecodeUInt8(), Is.EqualTo(IceEncodingDefinitions.TagEndMarker));
            Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
        }
    }

    [Test]
    public void Decode_class_with_compact_format()
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new IceEncoder(buffer);

        encoder.EncodeSize(1); // Instance marker
        encoder.EncodeUInt8(
            (byte)IceEncodingDefinitions.TypeIdKind.String |  // The first Slice include a type Id
            (byte)IceEncodingDefinitions.SliceFlags.IsLastSlice);
        encoder.EncodeString(typeof(MyClassA).GetIceTypeId()!);

        // MyClassA.theB field encoded inline (2 Slices)

        encoder.EncodeSize(1); // Instance marker

        // MyClassA.theB - First Slice
        encoder.EncodeUInt8((byte)IceEncodingDefinitions.TypeIdKind.String);
        encoder.EncodeString(typeof(MyClassB).GetIceTypeId()!);
        encoder.EncodeSize(0); // null instance

        // MyClassA.theB - Second Slice
        encoder.EncodeUInt8((byte)IceEncodingDefinitions.SliceFlags.IsLastSlice);
        encoder.EncodeSize(0); // null instance
        encoder.EncodeSize(0); // null instance

        // MyClassA.theC field encoded inline (1 Slice)

        encoder.EncodeSize(1); // Instance marker

        // MyClassA.theC - First Slice
        encoder.EncodeUInt8(
            (byte)IceEncodingDefinitions.TypeIdKind.String | (byte)IceEncodingDefinitions.SliceFlags.IsLastSlice);
        encoder.EncodeString(typeof(MyClassC).GetIceTypeId()!);
        encoder.EncodeSize(0); // null instance

        var decoder = new IceDecoder(
            buffer.WrittenMemory,
            activator: IActivator.FromAssembly(typeof(MyClassA).Assembly));

        // Act
        MyClassA theA = decoder.DecodeClass<MyClassA>()!;

        // Assert
        Assert.That(theA.TheB, Is.Not.Null);
        Assert.That(theA.TheB, Is.TypeOf<MyClassB>());
        Assert.That(theA.TheB!.TheA, Is.Null);
        Assert.That(theA.TheB.TheB, Is.Null);
        Assert.That(theA.TheB.TheC, Is.Null);

        Assert.That(theA.TheC, Is.Not.Null);
        Assert.That(theA.TheC, Is.TypeOf<MyClassC>());
        Assert.That(theA.TheC!.TheB, Is.Null);

        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Decode_class_with_sliced_format()
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new IceEncoder(buffer);

        encoder.EncodeSize(1); // Instance marker

        encoder.EncodeUInt8(
            (byte)IceEncodingDefinitions.TypeIdKind.String |
            (byte)IceEncodingDefinitions.SliceFlags.HasIndirectionTable | // The sliced format includes an indirection
            (byte)IceEncodingDefinitions.SliceFlags.HasSliceSize |        // table and the Slice size for MyClassA
            (byte)IceEncodingDefinitions.SliceFlags.IsLastSlice);

        encoder.EncodeString(typeof(MyClassA).GetIceTypeId()!);

        encoder.EncodeInt32(6); // Slice size (int size + two references)
        encoder.EncodeSize(1);  // Reference the first entry in the indirection table
        encoder.EncodeSize(2);  // Reference the second entry in the indirection table

        encoder.EncodeSize(2);  // Size of the indirection table

        // MyClassA.theB field encoded in the indirection table (2 Slices)

        encoder.EncodeSize(1); // Instance marker

        // theB - First Slice
        encoder.EncodeUInt8(
            (byte)IceEncodingDefinitions.TypeIdKind.String |
            (byte)IceEncodingDefinitions.SliceFlags.HasSliceSize);
        encoder.EncodeString(typeof(MyClassB).GetIceTypeId()!);
        encoder.EncodeInt32(5); // Slice size (int size + one reference)
        encoder.EncodeSize(0); // null instance

        // theB - Second Slice
        encoder.EncodeUInt8(
            (byte)IceEncodingDefinitions.TypeIdKind.Index |
            (byte)IceEncodingDefinitions.SliceFlags.HasSliceSize |
            (byte)IceEncodingDefinitions.SliceFlags.IsLastSlice);
        encoder.EncodeSize(1); // TypeId encoded as an index as this TypeId already appears
                               // with the first instance.
        encoder.EncodeInt32(6); // Slice size (int size + two references)
        encoder.EncodeSize(0); // null instance
        encoder.EncodeSize(0); // null instance

        // MyClassA.theC field encoded in the indirection table (1 Slices)
        encoder.EncodeSize(1); // Instance marker

        // theC - First Slice
        encoder.EncodeUInt8(
            (byte)IceEncodingDefinitions.TypeIdKind.String |
            (byte)IceEncodingDefinitions.SliceFlags.HasSliceSize |
            (byte)IceEncodingDefinitions.SliceFlags.IsLastSlice);
        encoder.EncodeString(typeof(MyClassC).GetIceTypeId()!);
        encoder.EncodeInt32(5); // Slice size (int size + one reference)
        encoder.EncodeSize(0); // null instance

        var decoder = new IceDecoder(
            buffer.WrittenMemory,
            activator: IActivator.FromAssembly(typeof(MyClassA).Assembly));

        // Act
        MyClassA theA = decoder.DecodeClass<MyClassA>()!;

        // Assert
        Assert.That(theA.TheB, Is.Not.Null);
        Assert.That(theA.TheB, Is.TypeOf<MyClassB>());
        Assert.That(theA.TheB!.TheA, Is.Null);
        Assert.That(theA.TheB.TheB, Is.Null);
        Assert.That(theA.TheB.TheC, Is.Null);

        Assert.That(theA.TheC, Is.Not.Null);
        Assert.That(theA.TheC, Is.TypeOf<MyClassC>());
        Assert.That(theA.TheC!.TheB, Is.Null);

        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Decode_class_graph_with_compact_format()
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new IceEncoder(buffer);

        // The class graph is encoded inline when using the compact format
        // theA index 2
        // theB index 3
        // theC index 4
        encoder.EncodeSize(1); // Instance marker

        encoder.EncodeUInt8((byte)IceEncodingDefinitions.TypeIdKind.String | (byte)IceEncodingDefinitions.SliceFlags.IsLastSlice);
        encoder.EncodeString(typeof(MyClassA).GetIceTypeId()!);

        // MyClassA.theB field encoded inline (2 Slices)

        encoder.EncodeSize(1); // Instance marker

        // theB - First Slice
        encoder.EncodeUInt8((byte)IceEncodingDefinitions.TypeIdKind.String);
        encoder.EncodeString(typeof(MyClassB).GetIceTypeId()!);
        encoder.EncodeSize(0); // null reference

        // theB - Second Slice
        encoder.EncodeUInt8((byte)IceEncodingDefinitions.SliceFlags.IsLastSlice);
        encoder.EncodeSize(0); // null reference

        // theB.theC instance encoded inline
        encoder.EncodeSize(1); // Instance marker

        // MyClassA.theB.theC encoded inline (1 Slice)
        encoder.EncodeUInt8(
            (byte)IceEncodingDefinitions.TypeIdKind.String | (byte)IceEncodingDefinitions.SliceFlags.IsLastSlice);
        encoder.EncodeString(typeof(MyClassC).GetIceTypeId()!);
        encoder.EncodeSize(3); // reference to instance with index 3

        // MyClassA.TheC encoded as an index
        encoder.EncodeSize(4); // reference to instance with index 4

        var decoder = new IceDecoder(
            buffer.WrittenMemory,
            activator: IActivator.FromAssembly(typeof(MyClassA).Assembly));

        // Act
        MyClassA theA = decoder.DecodeClass<MyClassA>()!;

        // Assert
        Assert.That(theA.TheB, Is.Not.Null);
        Assert.That(theA.TheB, Is.TypeOf<MyClassB>());
        Assert.That(theA.TheB!.TheA, Is.Null);
        Assert.That(theA.TheB.TheB, Is.Null);
        Assert.That(theA.TheB.TheC, Is.Not.Null);

        Assert.That(theA.TheC, Is.Not.Null);
        Assert.That(theA.TheC, Is.TypeOf<MyClassC>());
        Assert.That(theA.TheC!.TheB, Is.Not.Null);

        Assert.That(theA.TheB.TheC, Is.EqualTo(theA.TheC));
        Assert.That(theA.TheC.TheB, Is.EqualTo(theA.TheB));

        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Decode_class_graph_with_sliced_format()
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new IceEncoder(buffer, classFormat: ClassFormat.Sliced);
        encoder.EncodeSize(1); // Instance marker
        encoder.EncodeUInt8(
            (byte)IceEncodingDefinitions.TypeIdKind.String |
            (byte)IceEncodingDefinitions.SliceFlags.HasIndirectionTable |
            (byte)IceEncodingDefinitions.SliceFlags.HasSliceSize |
            (byte)IceEncodingDefinitions.SliceFlags.IsLastSlice);
        encoder.EncodeString(typeof(MyClassA).GetIceTypeId()!);
        encoder.EncodeInt32(6); // Slice size

        encoder.EncodeSize(1); // (reference 1st entry of the indirection table)
        encoder.EncodeSize(2); // (reference 2nd entry of the indirection table)

        encoder.EncodeSize(2); // Indirection table size

        // MyClassA.theB field encoded in the indirection table (2 Slices)
        encoder.EncodeSize(1); // Instance marker

        // First Slice
        encoder.EncodeUInt8(
            (byte)IceEncodingDefinitions.TypeIdKind.String |
            (byte)IceEncodingDefinitions.SliceFlags.HasSliceSize);

        // theB - First Slice
        encoder.EncodeString(typeof(MyClassB).GetIceTypeId()!);
        encoder.EncodeInt32(5); // Slice size (int size + one reference)
        encoder.EncodeSize(0); // null instance

        // theB - Second Slice
        encoder.EncodeUInt8(
            (byte)IceEncodingDefinitions.TypeIdKind.Index |
            (byte)IceEncodingDefinitions.SliceFlags.HasIndirectionTable |
            (byte)IceEncodingDefinitions.SliceFlags.HasSliceSize |
            (byte)IceEncodingDefinitions.SliceFlags.IsLastSlice);
        encoder.EncodeSize(1); // TypeId encoded as an index as this TypeId already appears
                               // with the first instance.
        encoder.EncodeInt32(6); // Slice size (int size + two references)
        encoder.EncodeSize(0); // null instance
        encoder.EncodeSize(1); // (reference 1st entry of the indirection table)

        encoder.EncodeSize(1); // Indirection table size

        // MyClassA.theB.theC field encoded in the indirection table (1 Slice)
        encoder.EncodeSize(1); // Instance marker
        encoder.EncodeUInt8(
            (byte)IceEncodingDefinitions.TypeIdKind.String |
            (byte)IceEncodingDefinitions.SliceFlags.HasIndirectionTable |
            (byte)IceEncodingDefinitions.SliceFlags.HasSliceSize |
            (byte)IceEncodingDefinitions.SliceFlags.IsLastSlice);

        // First Slice
        encoder.EncodeString(typeof(MyClassC).GetIceTypeId()!);
        encoder.EncodeInt32(5); // Slice size (int size + one reference)
        encoder.EncodeSize(1); // reference 1st entry of the indirection table

        encoder.EncodeSize(1); // Indirection table size

        encoder.EncodeSize(3); // Reference to index 3

        // MyClassA.theC encoded as an index
        encoder.EncodeSize(4); // Reference to index 4

        var decoder = new IceDecoder(
            buffer.WrittenMemory,
            activator: IActivator.FromAssembly(typeof(MyClassA).Assembly));

        // Act
        MyClassA theA = decoder.DecodeClass<MyClassA>()!;

        // Assert
        Assert.That(theA.TheB, Is.Not.Null);
        Assert.That(theA.TheB, Is.TypeOf<MyClassB>());
        Assert.That(theA.TheB!.TheA, Is.Null);
        Assert.That(theA.TheB.TheB, Is.Null);
        Assert.That(theA.TheB.TheC, Is.Not.Null);

        Assert.That(theA.TheC, Is.Not.Null);
        Assert.That(theA.TheC, Is.TypeOf<MyClassC>());
        Assert.That(theA.TheC!.TheB, Is.Not.Null);

        Assert.That(theA.TheB.TheC, Is.EqualTo(theA.TheC));
        Assert.That(theA.TheC.TheB, Is.EqualTo(theA.TheB));

        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Encode_decode_circular_graph([Values] ClassFormat classFormat)
    {
        var john = new Person();
        var yoko = new Person(john, null);
        john.Spouse = yoko;
        john.EmergencyContact = yoko;

        // Encode/decode it.
        var buffer = new MemoryBufferWriter(new byte[1024]);
        var encoder = new IceEncoder(buffer, classFormat: classFormat);
        encoder.EncodeClass(john);
        encoder.EncodeClass(yoko);

        var decoder = new IceDecoder(
            buffer.WrittenMemory,
            activator: IActivator.FromAssembly(typeof(Person).Assembly));

        // Act
        Person newJohn = decoder.DecodeClass<Person>()!;
        Person newYoko = decoder.DecodeClass<Person>()!;

        // Verify we get the same graph.
        Assert.That(newJohn.Spouse, Is.SameAs(newYoko));
        Assert.That(newJohn.EmergencyContact, Is.SameAs(newYoko));
        Assert.That(newYoko.Spouse, Is.SameAs(newJohn));
        Assert.That(newYoko.EmergencyContact, Is.Null);
    }

    [Test]
    public void Decode_class_with_compact_id_and_compact_format()
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new IceEncoder(buffer);

        encoder.EncodeSize(1); // Instance marker
        encoder.EncodeUInt8((byte)IceEncodingDefinitions.TypeIdKind.CompactId);
        encoder.EncodeSize(typeof(MyDerivedCompactClass).GetCompactIceTypeId()!.Value);

        encoder.EncodeUInt8((byte)IceEncodingDefinitions.SliceFlags.IsLastSlice);

        var decoder = new IceDecoder(
            buffer.WrittenMemory,
            activator: IActivator.FromAssembly(typeof(MyDerivedCompactClass).Assembly));

        // Act
        _ = decoder.DecodeClass<MyDerivedCompactClass>();

        // Assert
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Decode_class_with_compact_id_and_sliced_format()
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new IceEncoder(buffer, classFormat: ClassFormat.Sliced);

        encoder.EncodeSize(1); // Instance marker
        encoder.EncodeUInt8(
            (byte)IceEncodingDefinitions.TypeIdKind.CompactId |
            (byte)IceEncodingDefinitions.SliceFlags.HasSliceSize);
        encoder.EncodeSize(typeof(MyDerivedCompactClass).GetCompactIceTypeId()!.Value);
        encoder.EncodeInt32(4); // Empty Slice 4 bytes

        encoder.EncodeUInt8(
            (byte)IceEncodingDefinitions.TypeIdKind.CompactId |
            (byte)IceEncodingDefinitions.SliceFlags.HasSliceSize |
            (byte)IceEncodingDefinitions.SliceFlags.IsLastSlice);
        encoder.EncodeSize(typeof(MyCompactClass).GetCompactIceTypeId()!.Value);
        encoder.EncodeInt32(4); // Empty Slice 4 bytes

        var decoder = new IceDecoder(
            buffer.WrittenMemory,
            activator: IActivator.FromAssembly(typeof(MyDerivedCompactClass).Assembly));

        // Act
        _ = decoder.DecodeClass<MyDerivedCompactClass>();

        // Assert
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Decode_class_with_tagged_fields_and_compact_format(
        [Values(10, null)] int? a,
        [Values("hello world!", null)] string? b,
        [Values(20, null)] long? c)
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new IceEncoder(buffer);

        encoder.EncodeSize(1); // Instance marker
        byte sliceFlags = (byte)IceEncodingDefinitions.TypeIdKind.String;
        if (b is not null || c is not null)
        {
            sliceFlags |= (byte)IceEncodingDefinitions.SliceFlags.HasTaggedFields;
        }
        encoder.EncodeUInt8(sliceFlags);

        encoder.EncodeString(typeof(MyDerivedClassWithTaggedFields).GetIceTypeId()!);

        // MyDerivedClassWithTaggedFields.B
        if (b is not null)
        {
            encoder.EncodeTagged(
                20,
                TagFormat.OptimizedVSize,
                b,
                (ref IceEncoder encoder, string value) => encoder.EncodeString(value));
        }

        if (c is not null)
        {
            // Additional tagged field not defined in Slice
            encoder.EncodeTagged(
                30,
                TagFormat.F8,
                c.Value,
                (ref IceEncoder encoder, long value) => encoder.EncodeInt64(value));
        }

        if (b is not null || c is not null)
        {
            encoder.EncodeUInt8(IceEncodingDefinitions.TagEndMarker);
        }

        sliceFlags = (byte)IceEncodingDefinitions.SliceFlags.IsLastSlice;
        if (a is not null)
        {
            sliceFlags |= (byte)IceEncodingDefinitions.SliceFlags.HasTaggedFields;
        }
        encoder.EncodeUInt8(sliceFlags);

        // MyClassWithTaggedFields.A
        if (a is not null)
        {
            encoder.EncodeTagged(
                10,
                TagFormat.F4,
                a.Value,
                (ref IceEncoder encoder, int value) => encoder.EncodeInt32(value));
            encoder.EncodeUInt8(IceEncodingDefinitions.TagEndMarker);
        }
        var decoder = new IceDecoder(
            buffer.WrittenMemory,
            activator: IActivator.FromAssembly(typeof(MyDerivedClassWithTaggedFields).Assembly));

        // Act
        var classWithTaggedFields = decoder.DecodeClass<MyDerivedClassWithTaggedFields>();

        // Assert
        Assert.That(classWithTaggedFields?.A, Is.EqualTo(a));
        Assert.That(classWithTaggedFields?.B, Is.EqualTo(b));
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }
}
