// Copyright (c) ZeroC, Inc.

using IceRpc.Slice;
using IceRpc.Slice.Internal;
using NUnit.Framework;

namespace IceRpc.Tests.Slice;

[Parallelizable(scope: ParallelScope.All)]
public class SlicingTests
{
    [Test]
    public void Decoding_a_class_skips_and_preserves_unknown_slices([Values] bool partialSlicing)
    {
        // Arrange
        var p2 = new SlicingMostDerivedClass("p2-m1", "p2-m2", null);
        var p1 = new SlicingMostDerivedClass("p1-m1", "p1-m2", p2);

        IActivator? slicingActivator = null;
        if (partialSlicing)
        {
            // Create an activator that excludes 'SlicingMostDerivedClass' type ID and ensure that the class is
            // sliced and the Slices are preserved.
            slicingActivator = new SlicingActivator(
                SliceDecoder.GetActivator(typeof(SlicingMostDerivedClass).Assembly),
                excludeTypeId: typeof(SlicingMostDerivedClass).GetSliceTypeId());
        }
        SliceClass? r1 = EncodeAndDecodeClass<SliceClass>(p1, slicingActivator);

        // Act

        // Encode the sliced class and decode again using an activator that knows all the type IDs, the decoded class
        // should contain the preserved Slices.
        SlicingMostDerivedClass r2 = EncodeAndDecodeClass<SlicingMostDerivedClass>(
            p1,
            SliceDecoder.GetActivator(typeof(SlicingMostDerivedClass).Assembly));

        // Assert
        Assert.That(r1, partialSlicing ? Is.TypeOf<SlicingDerivedClass>() : Is.TypeOf<UnknownSlicedClass>());
        Assert.That(r1.UnknownSlices, Is.Not.Empty);

        Assert.That(r2.UnknownSlices, Is.Empty);
        Assert.That(r2.M1, Is.EqualTo("p1-m1"));
        Assert.That(r2.M2, Is.EqualTo("p1-m2"));
        Assert.That(r2.M3, Is.InstanceOf<SlicingMostDerivedClass>());
        var r3 = (SlicingMostDerivedClass)r2.M3;
        Assert.That(r3!.M1, Is.EqualTo("p2-m1"));
        Assert.That(r3.M2, Is.EqualTo("p2-m2"));
        Assert.That(r3!.M3, Is.Null);
    }

    [Test]
    public void Decoding_a_class_with_compact_id_skips_and_preserves_unknown_slices([Values] bool partialSlicing)
    {
        // Arrange
        var p2 = new SlicingMostDerivedClassWithCompactId("p2-m1", "p2-m2", null);
        var p1 = new SlicingMostDerivedClassWithCompactId("p1-m1", "p1-m2", p2);

        IActivator? slicingActivator = null;
        if (partialSlicing)
        {
            // Create an activator that excludes 'SlicingMostDerivedClassWithCompactId' type ID and ensure that the
            // class is sliced and the Slices are preserved.
            slicingActivator = new SlicingActivator(
                SliceDecoder.GetActivator(typeof(SlicingMostDerivedClassWithCompactId).Assembly),
                excludeTypeId: typeof(SlicingMostDerivedClassWithCompactId).GetCompactSliceTypeId().ToString());
        }
        SliceClass? r1 = EncodeAndDecodeClass<SliceClass>(p1, slicingActivator);

        // Act

        // Encode the sliced class and decode again using an activator that knows all the type IDs, the decoded class
        // should contain the preserved Slices.
        SlicingMostDerivedClassWithCompactId r2 = EncodeAndDecodeClass<SlicingMostDerivedClassWithCompactId>(
            p1,
            SliceDecoder.GetActivator(typeof(SlicingMostDerivedClassWithCompactId).Assembly));

        // Assert
        Assert.That(
            r1,
            partialSlicing ? Is.TypeOf<SlicingDerivedClassWithCompactId>() : Is.TypeOf<UnknownSlicedClass>());
        Assert.That(r1.UnknownSlices, Is.Not.Empty);

        Assert.That(r2.UnknownSlices, Is.Empty);
        Assert.That(r2.M1, Is.EqualTo("p1-m1"));
        Assert.That(r2.M2, Is.EqualTo("p1-m2"));
        Assert.That(r2.M3, Is.InstanceOf<SlicingMostDerivedClassWithCompactId>());
        var r3 = (SlicingMostDerivedClassWithCompactId)r2.M3;
        Assert.That(r3!.M1, Is.EqualTo("p2-m1"));
        Assert.That(r3.M2, Is.EqualTo("p2-m2"));
        Assert.That(r3!.M3, Is.Null);
    }

    [Test]
    public void Decoding_an_exception_skips_unknown_slices([Values] bool partialSlicing)
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[1024 * 1024]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice1, classFormat: ClassFormat.Sliced);

        var p1 = new SlicingMostDerivedException("most-derived", "derived", "base");
        p1.Encode(ref encoder);

        // Create an activator that exclude 'SlicingMostDerivedException' type ID and ensure that the class is decoded
        // as 'slicingDerivedException' which is the base type.
        SlicingActivator? slicingActivator = null;
        if (partialSlicing)
        {
            slicingActivator = new SlicingActivator(
                SliceDecoder.GetActivator(typeof(SlicingMostDerivedException).Assembly),
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

    private static T EncodeAndDecodeClass<T>(SliceClass sliceClass, IActivator? activator)
        where T : SliceClass
    {
        var buffer = new MemoryBufferWriter(new byte[1024 * 1024]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice1, classFormat: ClassFormat.Sliced);

        encoder.EncodeClass(sliceClass);

        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice1, activator: activator);
        T value = decoder.DecodeClass<T>();
        decoder.CheckEndOfBuffer(skipTaggedParams: false);
        return value;
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
