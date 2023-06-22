// Copyright (c) ZeroC, Inc.

using IceRpc.Slice;
using IceRpc.Tests.Common;
using NUnit.Framework;

namespace IceRpc.Tests.Slice;

[Parallelizable(scope: ParallelScope.All)]
public class SlicingTests
{
    [Test]
    public void Decoding_a_class_skips_and_preserves_unknown_slices([Values] bool partialSlicing)
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[1024 * 1024]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice1, classFormat: ClassFormat.Sliced);

        var p2 = new SlicingMostDerivedClass("p2-m1", "p2-m2", null, null);
        var p1 = new SlicingMostDerivedClass("p1-m1", "p1-m2", p2, p2);
        encoder.EncodeClass(p1);

        IActivator? slicingActivator = null;
        if (partialSlicing)
        {
            // Create an activator that excludes 'SlicingMostDerivedClass' type ID and ensure that the class is
            // sliced and the Slices are preserved.
            slicingActivator = new SlicingActivator(
                IActivator.FromAssembly(typeof(SlicingMostDerivedClass).Assembly),
                excludeTypeId: typeof(SlicingMostDerivedClass).GetSliceTypeId());
        }

        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice1, activator: slicingActivator);
        SliceClass? r1 = decoder.DecodeClass<SliceClass>();

        // Encode the sliced class
        buffer = new MemoryBufferWriter(new byte[1024 * 1024]);
        encoder = new SliceEncoder(buffer, SliceEncoding.Slice1, classFormat: ClassFormat.Sliced);
        encoder.EncodeClass(r1!);

        // Act

        // Decode again using an activator that knows all the type IDs, the decoded class should contain the preserved
        // Slices.
        decoder = new SliceDecoder(
            buffer.WrittenMemory,
            SliceEncoding.Slice1,
            activator: IActivator.FromAssembly(typeof(SlicingMostDerivedClass).Assembly));

        SlicingMostDerivedClass r2 = decoder.DecodeClass<SlicingMostDerivedClass>();
        decoder.CheckEndOfBuffer(skipTaggedParams: false);

        // Assert
        Assert.That(r1, partialSlicing ? Is.TypeOf<SlicingDerivedClass>() : Is.TypeOf<UnknownSlicedClass>());
        Assert.That(r1.UnknownSlices, Is.Not.Empty);
        if (partialSlicing)
        {
            Assert.That(((SlicingDerivedClass)r1).M3, Is.TypeOf<SlicingDerivedClass>());
            Assert.That(((SlicingDerivedClass)r1).M3!.M1, Is.EqualTo("p2-m1"));
        }

        Assert.That(r2.UnknownSlices, Is.Empty);
        Assert.That(r2.M1, Is.EqualTo("p1-m1"));
        Assert.That(r2.M2, Is.EqualTo("p1-m2"));
        Assert.That(r2.M3, Is.InstanceOf<SlicingMostDerivedClass>());
        Assert.That(r2.M3, Is.SameAs(r2.M4));
        var r3 = (SlicingMostDerivedClass)r2.M3!;
        Assert.That(r3!.M1, Is.EqualTo("p2-m1"));
        Assert.That(r3.M2, Is.EqualTo("p2-m2"));
        Assert.That(r3!.M3, Is.Null);
    }

    [Test]
    public void Decoding_a_class_with_compact_id_skips_and_preserves_unknown_slices([Values] bool partialSlicing)
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[1024 * 1024]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice1, classFormat: ClassFormat.Sliced);

        var p2 = new SlicingMostDerivedClassWithCompactId("p2-m1", "p2-m2", null);
        var p1 = new SlicingMostDerivedClassWithCompactId("p1-m1", "p1-m2", p2);
        encoder.EncodeClass(p1);

        IActivator? slicingActivator = null;
        if (partialSlicing)
        {
            // Create an activator that excludes 'SlicingMostDerivedClassWithCompactId' type ID and ensure that the class is
            // sliced and the Slices are preserved.
            slicingActivator = new SlicingActivator(
                IActivator.FromAssembly(typeof(SlicingMostDerivedClassWithCompactId).Assembly),
                excludeTypeId: typeof(SlicingMostDerivedClassWithCompactId).GetCompactSliceTypeId().ToString());
        }

        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice1, activator: slicingActivator);
        SliceClass? r1 = decoder.DecodeClass<SliceClass>();

        // Encode the sliced class
        buffer = new MemoryBufferWriter(new byte[1024 * 1024]);
        encoder = new SliceEncoder(buffer, SliceEncoding.Slice1, classFormat: ClassFormat.Sliced);
        encoder.EncodeClass(r1!);

        // Act

        // Decode again using an activator that knows all the type IDs, the decoded class should contain the preserved
        // Slices.
        decoder = new SliceDecoder(
            buffer.WrittenMemory,
            SliceEncoding.Slice1,
            activator: IActivator.FromAssembly(typeof(SlicingMostDerivedClassWithCompactId).Assembly));

        SlicingMostDerivedClassWithCompactId r2 = decoder.DecodeClass<SlicingMostDerivedClassWithCompactId>();
        decoder.CheckEndOfBuffer(skipTaggedParams: false);

        // Assert
        Assert.That(r1, partialSlicing ? Is.TypeOf<SlicingDerivedClassWithCompactId>() : Is.TypeOf<UnknownSlicedClass>());
        Assert.That(r1.UnknownSlices, Is.Not.Empty);

        Assert.That(r2.UnknownSlices, Is.Empty);
        Assert.That(r2.M1, Is.EqualTo("p1-m1"));
        Assert.That(r2.M2, Is.EqualTo("p1-m2"));
        Assert.That(r2.M3, Is.InstanceOf<SlicingMostDerivedClassWithCompactId>());
        var r3 = (SlicingMostDerivedClassWithCompactId)r2.M3!;
        Assert.That(r3!.M1, Is.EqualTo("p2-m1"));
        Assert.That(r3.M2, Is.EqualTo("p2-m2"));
        Assert.That(r3!.M3, Is.Null);
    }

    [Test]
    public void Decoding_a_class_with_tagged_fields_skips_and_preserves_unknown_slices([Values] bool partialSlicing)
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[1024 * 1024]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice1, classFormat: ClassFormat.Sliced);

        var p2 = new SlicingClassWithTaggedFields("p2-m1", "p2-m2", null, null, "p2-m5");
        var p1 = new SlicingClassWithTaggedFields("p1-m1", "p1-m2", p2, p2, "p1-m5");
        encoder.EncodeClass(p1);

        IActivator? slicingActivator = null;
        if (partialSlicing)
        {
            // Create an activator that excludes 'SlicingClassWithTaggedFields' type ID and ensure that the class is
            // sliced and the Slices are preserved.
            slicingActivator = new SlicingActivator(
                IActivator.FromAssembly(typeof(SlicingClassWithTaggedFields).Assembly),
                excludeTypeId: typeof(SlicingClassWithTaggedFields).GetSliceTypeId());
        }

        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice1, activator: slicingActivator);
        SliceClass? r1 = decoder.DecodeClass<SliceClass>();

        // Encode the sliced class
        buffer = new MemoryBufferWriter(new byte[1024 * 1024]);
        encoder = new SliceEncoder(buffer, SliceEncoding.Slice1, classFormat: ClassFormat.Sliced);
        encoder.EncodeClass(r1!);

        // Act

        // Decode again using an activator that knows all the type IDs, the decoded class should contain the preserved
        // Slices.
        decoder = new SliceDecoder(
            buffer.WrittenMemory,
            SliceEncoding.Slice1,
            activator: IActivator.FromAssembly(typeof(SlicingClassWithTaggedFields).Assembly));

        SlicingClassWithTaggedFields r2 = decoder.DecodeClass<SlicingClassWithTaggedFields>();
        decoder.CheckEndOfBuffer(skipTaggedParams: false);

        // Assert
        Assert.That(r1, partialSlicing ? Is.TypeOf<SlicingDerivedClass>() : Is.TypeOf<UnknownSlicedClass>());
        Assert.That(r1.UnknownSlices, Is.Not.Empty);

        Assert.That(r2.UnknownSlices, Is.Empty);
        Assert.That(r2.M1, Is.EqualTo("p1-m1"));
        Assert.That(r2.M2, Is.EqualTo("p1-m2"));
        Assert.That(r2.M3, Is.InstanceOf<SlicingClassWithTaggedFields>());
        Assert.That(r2.M5, Is.EqualTo("p1-m5"));
        var r3 = (SlicingClassWithTaggedFields)r2.M3!;
        Assert.That(r3!.M1, Is.EqualTo("p2-m1"));
        Assert.That(r3.M2, Is.EqualTo("p2-m2"));
        Assert.That(r3!.M3, Is.Null);
        Assert.That(r3.M5, Is.EqualTo("p2-m5"));
    }

    [Test]
    public void Decoding_an_exception_skips_unknown_slices([Values] bool partialSlicing)
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[1024 * 1024]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice1, classFormat: ClassFormat.Sliced);

        var p1 = new SlicingMostDerivedException("most-derived-m1", "most-derived-m2", new SlicingBaseClass("base-m1"));
        p1.Encode(ref encoder);

        // Create an activator that exclude 'SlicingMostDerivedException' type ID and ensure that the class is decoded
        // as 'slicingDerivedException' which is the base type.
        SlicingActivator? slicingActivator = null;
        if (partialSlicing)
        {
            slicingActivator = new SlicingActivator(
                IActivator.FromAssembly(typeof(SlicingMostDerivedException).Assembly),
                excludeTypeId: typeof(SlicingMostDerivedException).GetSliceTypeId());
        }
        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice1, activator: slicingActivator);

        // Act
        DispatchException? dispatchException = decoder.DecodeUserException();
        decoder.CheckEndOfBuffer(skipTaggedParams: false);

        // Assert
        Assert.That(dispatchException, Is.Not.Null);

        if (partialSlicing)
        {
            Assert.That(dispatchException, Is.TypeOf<SlicingDerivedException>());
            var slicingDerivedException = (SlicingDerivedException)dispatchException;
            Assert.That(slicingDerivedException.M1, Is.EqualTo(p1.M1));
            Assert.That(slicingDerivedException.M2, Is.EqualTo(p1.M2));
        }
        else
        {
            Assert.That(dispatchException, Is.TypeOf<DispatchException>());
        }
    }

    [Test]
    public void Skipped_instances_triggers_graph_max_depth_check()
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[1024 * 1024]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice1, classFormat: ClassFormat.Sliced);

        var p5 = new SlicingMostDerivedClass("p5-m1", "p5-m2", null, null);
        var p4 = new SlicingMostDerivedClass("p4-m1", "p4-m2", p5, p5);
        var p3 = new SlicingMostDerivedClass("p3-m1", "p3-m2", p4, p4);
        var p2 = new SlicingMostDerivedClass("p2-m1", "p2-m2", p3, p3);
        var p1 = new SlicingMostDerivedClass("p1-m1", "p1-m2", p2, p2);
        encoder.EncodeClass(p1);

        Assert.That(
            () =>
            {
                var decoder = new SliceDecoder(
                    buffer.WrittenMemory,
                    SliceEncoding.Slice1,
                    maxDepth: 3);
                SliceClass? r1 = decoder.DecodeClass<SliceClass>();
            },
            Throws.TypeOf<InvalidDataException>());
    }

    /// <summary>An activator that delegates to another activator except for the Sliced type IDs, for which it
    /// returns null instances. This allows testing class and exception slicing.</summary>
    private sealed class SlicingActivator : IActivator
    {
        private readonly IActivator _decoratee;
        private readonly string? _excludeTypeId;

        public SlicingActivator(IActivator activator, string? excludeTypeId = null)
        {
            _decoratee = activator;
            _excludeTypeId = excludeTypeId;
        }

        public object? CreateClassInstance(string typeId, ref SliceDecoder decoder) =>
            _excludeTypeId == typeId ? null : _decoratee.CreateClassInstance(typeId, ref decoder);

        public object? CreateExceptionInstance(string typeId, ref SliceDecoder decoder, string? message) =>
            _excludeTypeId == typeId ? null : _decoratee.CreateExceptionInstance(typeId, ref decoder, message);
    }
}
