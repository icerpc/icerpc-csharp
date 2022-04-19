// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using NUnit.Framework;

namespace IceRpc.Slice.Tests;

[Timeout(5000)]
[Parallelizable(ParallelScope.All)]
public sealed class ClassTests
{
    [Test]
    public void Encode_class_with_compact_format()
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice1);

        // Act
        encoder.EncodeClass(new MyClassA
        {
            TheB = new MyClassB(),
            TheC = new MyClassC(),
        });

        // Assert
        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice1);

        Assert.That(decoder.DecodeUInt8(), Is.EqualTo(1)); // Instance marker
        Assert.That(
            decoder.DecodeUInt8(), 
            Is.EqualTo(
                (byte)Slice1Definitions.TypeIdKind.String |  // The first Slice include a type Id
                (byte)Slice1Definitions.SliceFlags.IsLastSlice));
        Assert.That(decoder.DecodeString(), Is.EqualTo(MyClassA.SliceTypeId));

        // MyClassA.theB member encoded inline (2 Slices)

        Assert.That(decoder.DecodeUInt8(), Is.EqualTo(1)); // Instance marker

        // theB - First Slice
        Assert.That(decoder.DecodeUInt8(), Is.EqualTo((byte)Slice1Definitions.TypeIdKind.String));
        Assert.That(decoder.DecodeString(), Is.EqualTo(MyClassB.SliceTypeId));
        Assert.That(decoder.DecodeSize(), Is.EqualTo(0)); // null instance

        // theB - Second Slice
        Assert.That(
            decoder.DecodeUInt8(),
            Is.EqualTo((byte)Slice1Definitions.SliceFlags.IsLastSlice));
        Assert.That(decoder.DecodeSize(), Is.EqualTo(0)); // null instance
        Assert.That(decoder.DecodeSize(), Is.EqualTo(0)); // null instance

        // MyClassA.theC member encoded inline (1 Slice)

        Assert.That(decoder.DecodeUInt8(), Is.EqualTo(1));

        // theC - First Slice
        Assert.That(
            decoder.DecodeUInt8(),
            Is.EqualTo((byte)Slice1Definitions.TypeIdKind.String | (byte)Slice1Definitions.SliceFlags.IsLastSlice));
        Assert.That(decoder.DecodeString(), Is.EqualTo(MyClassC.SliceTypeId));
        Assert.That(decoder.DecodeSize(), Is.EqualTo(0)); // null instance
        
        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Encode_class_with_sliced_format()
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice1, classFormat: FormatType.Sliced);

        // Act
        encoder.EncodeClass(new MyClassA
        {
            TheB = new MyClassB(),
            TheC = new MyClassC(),
        });
        
        // Assert
        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice1);

        Assert.That(decoder.DecodeUInt8(), Is.EqualTo(1)); // Instance marker
        
        Assert.That(
            decoder.DecodeUInt8(),
            Is.EqualTo(
                (byte)Slice1Definitions.TypeIdKind.String |
                (byte)Slice1Definitions.SliceFlags.HasIndirectionTable | // The sliced format includes an indirection
                (byte)Slice1Definitions.SliceFlags.HasSliceSize |        // table and the Slice size for MyClassA
                (byte)Slice1Definitions.SliceFlags.IsLastSlice));

        Assert.That(decoder.DecodeString(), Is.EqualTo(MyClassA.SliceTypeId));

        Assert.That(decoder.DecodeInt32(), Is.EqualTo(6)); // Slice size (int size + two references)
        Assert.That(decoder.DecodeSize(), Is.EqualTo(1));  // Reference the first entry in the indirection table
        Assert.That(decoder.DecodeSize(), Is.EqualTo(2));  // Reference the second entry in the indirection table

        Assert.That(decoder.DecodeSize(), Is.EqualTo(2));  // Size of the indirection table

        // MyClassA.theB member encoded in the indirection table (2 Slices) 

        Assert.That(decoder.DecodeUInt8(), Is.EqualTo(1)); // Instance marker

        // theB - First Slice
        Assert.That(
            decoder.DecodeUInt8(),
            Is.EqualTo(
                (byte)Slice1Definitions.TypeIdKind.String |
                (byte)Slice1Definitions.SliceFlags.HasSliceSize));
        Assert.That(decoder.DecodeString(), Is.EqualTo(MyClassB.SliceTypeId));
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(5)); // Slice size (int size + one reference)
        Assert.That(decoder.DecodeSize(), Is.EqualTo(0)); // null instance

        // theB - Second Slice
        Assert.That(
            decoder.DecodeUInt8(),
            Is.EqualTo(
                (byte)Slice1Definitions.TypeIdKind.Index |
                (byte)Slice1Definitions.SliceFlags.HasSliceSize |
                (byte)Slice1Definitions.SliceFlags.IsLastSlice));
        Assert.That(decoder.DecodeSize(), Is.EqualTo(1)); // TypeId encoded as an index as this TypeId already appears
                                                          // with the first instance.
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(6)); // Slice size (int size + two references)
        Assert.That(decoder.DecodeSize(), Is.EqualTo(0)); // null instance
        Assert.That(decoder.DecodeSize(), Is.EqualTo(0)); // null instance

        // MyClassA.theB member encoded in the indirection table (2 Slices) 
        
        Assert.That(decoder.DecodeUInt8(), Is.EqualTo(1)); // Instance marker

        // theC - First Slice
        Assert.That(
            decoder.DecodeUInt8(),
            Is.EqualTo(
                (byte)Slice1Definitions.TypeIdKind.String |
                (byte)Slice1Definitions.SliceFlags.HasSliceSize |
                (byte)Slice1Definitions.SliceFlags.IsLastSlice));
        Assert.That(decoder.DecodeString(), Is.EqualTo(MyClassC.SliceTypeId));
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(5)); // Slice size (int size + one reference)
        Assert.That(decoder.DecodeSize(), Is.EqualTo(0)); // null instance

        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Encode_class_graph_with_compact_format()
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice1);
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

        var decoder = new SliceDecoder(buffer.WrittenMemory,SliceEncoding.Slice1);

        Assert.That(decoder.DecodeUInt8(), Is.EqualTo(1)); // Instance marker

        Assert.That(
            decoder.DecodeUInt8(),
            Is.EqualTo((byte)Slice1Definitions.TypeIdKind.String | (byte)Slice1Definitions.SliceFlags.IsLastSlice));
        Assert.That(decoder.DecodeString(), Is.EqualTo(MyClassA.SliceTypeId));

        // MyClassA.theB member encoded inline (2 Slices)

        Assert.That(decoder.DecodeUInt8(), Is.EqualTo(1)); // Instance marker

        // theB - First Slice
        Assert.That(decoder.DecodeUInt8(), Is.EqualTo((byte)Slice1Definitions.TypeIdKind.String));
        Assert.That(decoder.DecodeString(), Is.EqualTo(MyClassB.SliceTypeId));
        Assert.That(decoder.DecodeSize(), Is.EqualTo(0)); // null reference

        // theB - Second Slice
        Assert.That(decoder.DecodeUInt8(),Is.EqualTo((byte)Slice1Definitions.SliceFlags.IsLastSlice));
        Assert.That(decoder.DecodeSize(), Is.EqualTo(0)); // null reference

        // theB.theC instance encoded inline
        Assert.That(decoder.DecodeSize(), Is.EqualTo(1)); // Instance marker

        // MyClassA.theB.theC encoded inline (1 Slice)
        Assert.That(
            decoder.DecodeUInt8(),
            Is.EqualTo((byte)Slice1Definitions.TypeIdKind.String | (byte)Slice1Definitions.SliceFlags.IsLastSlice));
        Assert.That(decoder.DecodeString(), Is.EqualTo(MyClassC.SliceTypeId));
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
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice1, classFormat: FormatType.Sliced);
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

        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice1);

        Assert.That(decoder.DecodeUInt8(), Is.EqualTo(1)); // Instance marker
        Assert.That(
            decoder.DecodeUInt8(),
            Is.EqualTo(
                (byte)Slice1Definitions.TypeIdKind.String |
                (byte)Slice1Definitions.SliceFlags.HasIndirectionTable |
                (byte)Slice1Definitions.SliceFlags.HasSliceSize |
                (byte)Slice1Definitions.SliceFlags.IsLastSlice));
        Assert.That(decoder.DecodeString(), Is.EqualTo(MyClassA.SliceTypeId));
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(6)); // Slice size

        Assert.That(decoder.DecodeSize(), Is.EqualTo(1)); // (reference 1st entry of the indirection table)
        Assert.That(decoder.DecodeSize(), Is.EqualTo(2)); // (reference 2nd entry of the indirection table)

        Assert.That(decoder.DecodeSize(), Is.EqualTo(2)); // Indirection table size

        // MyClassA.theB member encoded in the indirection table (2 Slices)
        Assert.That(decoder.DecodeSize(), Is.EqualTo(1)); // Instance marker
        // First Slice
        Assert.That(
            decoder.DecodeUInt8(),
            Is.EqualTo(
                (byte)Slice1Definitions.TypeIdKind.String |
                (byte)Slice1Definitions.SliceFlags.HasSliceSize));

        // theB - First Slice
        Assert.That(decoder.DecodeString(), Is.EqualTo(MyClassB.SliceTypeId));
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(5)); // Slice size (int size + one reference)
        Assert.That(decoder.DecodeSize(), Is.EqualTo(0)); // null instance

        // theB - Second Slice
        Assert.That(
            decoder.DecodeUInt8(),
            Is.EqualTo(
                (byte)Slice1Definitions.TypeIdKind.Index |
                (byte)Slice1Definitions.SliceFlags.HasIndirectionTable |
                (byte)Slice1Definitions.SliceFlags.HasSliceSize |
                (byte)Slice1Definitions.SliceFlags.IsLastSlice));
        Assert.That(decoder.DecodeSize(), Is.EqualTo(1)); // TypeId encoded as an index as this TypeId already appears
                                                          // with the first instance.
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(6)); // Slice size (int size + two references)
        Assert.That(decoder.DecodeSize(), Is.EqualTo(0)); // null instance
        Assert.That(decoder.DecodeSize(), Is.EqualTo(1)); // (reference 1st entry of the indirection table)

        Assert.That(decoder.DecodeSize(), Is.EqualTo(1)); // Indirection table size

        // MyClassA.theB.theC member encoded in the indirection table (1 Slice)
        Assert.That(decoder.DecodeUInt8(), Is.EqualTo(1)); // Instance marker
        Assert.That(
            decoder.DecodeUInt8(),
            Is.EqualTo(
                (byte)Slice1Definitions.TypeIdKind.String |
                (byte)Slice1Definitions.SliceFlags.HasIndirectionTable |
                (byte)Slice1Definitions.SliceFlags.HasSliceSize |
                (byte)Slice1Definitions.SliceFlags.IsLastSlice));

        // First Slice
        Assert.That(decoder.DecodeString(), Is.EqualTo(MyClassC.SliceTypeId));
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
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice1);

        // Act
        encoder.EncodeClass(new MyDerivedCompactClass());

        // Assert
        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice1);

        Assert.That(decoder.DecodeUInt8(), Is.EqualTo(1));
        Assert.That(decoder.DecodeUInt8(), Is.EqualTo((byte)Slice1Definitions.TypeIdKind.CompactId));
        Assert.That(decoder.DecodeSize(), Is.EqualTo(typeof(MyDerivedCompactClass).GetCompactSliceTypeId()!.Value));

        Assert.That(decoder.DecodeUInt8(), Is.EqualTo((byte)Slice1Definitions.SliceFlags.IsLastSlice));

        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }

    [Test]
    public void Encode_class_with_compact_id_and_sliced_format()
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice1, classFormat: FormatType.Sliced);

        // Act
        encoder.EncodeClass(new MyDerivedCompactClass());

        // Assert
        var decoder = new SliceDecoder(
            buffer.WrittenMemory,
            SliceEncoding.Slice1,
            activator: SliceDecoder.GetActivator(typeof(MyDerivedCompactClass).Assembly));

        Assert.That(decoder.DecodeUInt8(), Is.EqualTo(1));
        Assert.That(
            decoder.DecodeUInt8(),
            Is.EqualTo(
                (byte)Slice1Definitions.TypeIdKind.CompactId |
                (byte)Slice1Definitions.SliceFlags.HasSliceSize));
        Assert.That(decoder.DecodeSize(), Is.EqualTo(typeof(MyDerivedCompactClass).GetCompactSliceTypeId()!.Value));
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(4)); // Empty Slice 4 bytes

        Assert.That(
            decoder.DecodeUInt8(),
            Is.EqualTo(
                (byte)Slice1Definitions.TypeIdKind.CompactId |
                (byte)Slice1Definitions.SliceFlags.HasSliceSize |
                (byte)Slice1Definitions.SliceFlags.IsLastSlice));
        Assert.That(decoder.DecodeSize(), Is.EqualTo(typeof(MyCompactClass).GetCompactSliceTypeId()!.Value));
        Assert.That(decoder.DecodeInt32(), Is.EqualTo(4)); // Empty Slice 4 bytes

        Assert.That(decoder.Consumed, Is.EqualTo(buffer.WrittenMemory.Length));
    }
}
