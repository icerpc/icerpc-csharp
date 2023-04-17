// Copyright (c) ZeroC, Inc.

using IceRpc.Slice;
using IceRpc.Slice.Internal;
using NUnit.Framework;
using System.Collections.Immutable;
using System.Reflection;

namespace IceRpc.Tests.Slice;

[Parallelizable(scope: ParallelScope.All)]
public class SlicingTests
{
    [Test]
    public void Unknown_class_decoded_as_derived_class()
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[1024 * 1024]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice1, classFormat: ClassFormat.Sliced);

        var p1 = new SlicingMostDerivedClass("most-derived", "derived", "base");
        encoder.EncodeClass(p1);

        // Create an activator that exclude 'SlicingMostDerivedClass' type ID and ensure that the class is decoded as
        // 'SlicingDerivedClass' which is the base type.
        var slicingActivator = new SlicingActivator(
            SliceDecoder.GetActivator(
                new Assembly[]
                {
                    typeof(SlicingMostDerivedClass).Assembly,
                    typeof(SlicingDerivedClass).Assembly,
                    typeof(SlicingBaseClass).Assembly
                }),
            slicedTypeIds: ImmutableList.Create(typeof(SlicingMostDerivedClass).GetSliceTypeId()!));
        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice1, activator: slicingActivator);

        // Act
        SliceClass? anyClass = decoder.DecodeClass<SliceClass>();
        decoder.CheckEndOfBuffer(skipTaggedParams: false);

        // Assert
        Assert.That(anyClass, Is.Not.Null);
        Assert.That(anyClass, Is.InstanceOf<SlicingDerivedClass>());
        var SlicingDerivedClass = (SlicingDerivedClass)anyClass;
        Assert.That(SlicingDerivedClass, Is.Not.Null);
        Assert.That(SlicingDerivedClass.UnknownSlices, Is.Not.Empty);
        Assert.That(SlicingDerivedClass.M1, Is.EqualTo(p1.M1));
        Assert.That(SlicingDerivedClass.M2, Is.EqualTo(p1.M2));
    }

    [Test]
    public void Slicing_preserve_unknown_slices([Values] bool partialSlicing)
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[1024 * 1024]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice1, classFormat: ClassFormat.Sliced);

        var p2 = new SlicingPreservedDerivedClass1("p2-m1", "p2-m2", new SlicingBaseClass("base"));
        var p1 = new SlicingPreservedDerivedClass1("p1-m1", "p1-m2", p2);
        encoder.EncodeClass(p1);

        IActivator? slicingActivator = null;
        if (partialSlicing)
        {
            // Create an activator that excludes 'SlicingPreservedDerivedClass1' type ID and ensure that the class is
            // sliced and the Slices are preserved.
            slicingActivator = new SlicingActivator(
                SliceDecoder.GetActivator(typeof(SlicingPreservedDerivedClass1).Assembly),
                slicedTypeIds: ImmutableList.Create(typeof(SlicingPreservedDerivedClass1).GetSliceTypeId()!));
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
            activator: SliceDecoder.GetActivator(typeof(SlicingPreservedDerivedClass1).Assembly));

        SlicingPreservedDerivedClass1 r2 = decoder.DecodeClass<SlicingPreservedDerivedClass1>();
        decoder.CheckEndOfBuffer(skipTaggedParams: false);

        // Assert
        Assert.That(r1, partialSlicing ? Is.TypeOf<SlicingPreservedClass>() : Is.TypeOf<UnknownSlicedClass>());
        Assert.That(r1.UnknownSlices, Is.Not.Empty);

        Assert.That(r2.UnknownSlices, Is.Empty);
        Assert.That(r2.M1, Is.EqualTo("p1-m1"));
        Assert.That(r2.M2, Is.EqualTo("p1-m2"));
        Assert.That(r2.M3, Is.InstanceOf<SlicingPreservedDerivedClass1>());
        var r3 = (SlicingPreservedDerivedClass1)r2.M3;
        Assert.That(r3.M1, Is.EqualTo("p2-m1"));
        Assert.That(r3.M2, Is.EqualTo("p2-m2"));
        Assert.That(r3.M3.M1, Is.EqualTo("base"));
    }

    /// <summary>An activator that delegates to another activator except for the Sliced type IDs, for which it
    /// returns null instances. This allows testing class and exception slicing.</summary>
    private sealed class SlicingActivator : IActivator
    {
        private readonly IActivator _decoratee;
        private readonly ImmutableList<string> _slicedTypeIds;

        public SlicingActivator(IActivator activator, ImmutableList<string>? slicedTypeIds = null)
        {
            _decoratee = activator;
            _slicedTypeIds = slicedTypeIds ?? ImmutableList<string>.Empty;
        }

        public object? CreateClassInstance(string typeId, ref SliceDecoder decoder) =>
            _slicedTypeIds.Contains(typeId) ? null : _decoratee.CreateClassInstance(typeId, ref decoder);

        public object? CreateExceptionInstance(string typeId, ref SliceDecoder decoder, string? message) =>
            _slicedTypeIds.Contains(typeId) ? null : _decoratee.CreateExceptionInstance(typeId, ref decoder, message);
    }
}
