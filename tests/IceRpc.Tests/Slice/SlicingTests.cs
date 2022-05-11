// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using NUnit.Framework;
using System.Collections.Immutable;
using System.Reflection;

namespace IceRpc.Slice.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class SlicingTests
{
    [Test]
    public void Unknown_class_decoded_as_derived_class()
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[1024 * 1024]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice1, classFormat: ClassFormat.Sliced);

        var p1 = new MyMostDerivedClass("most-derived", "derived", "base");
        encoder.EncodeClass(p1);

        // Create an activator that exclude 'MyMostDerivedClass' type ID and ensure that the class is decoded as
        // 'MyDerivedClass' which is the base type.
        var slicingActivator = new SlicingActivator(
            SliceDecoder.GetActivator(
                new Assembly[]
                {
                    typeof(MyMostDerivedClass).Assembly,
                    typeof(MyDerivedClass).Assembly,
                    typeof(MyBaseClass).Assembly
                }),
            slicedTypeIds: ImmutableList.Create(MyMostDerivedClass.SliceTypeId));
        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice1, activator: slicingActivator);

        // Act
        AnyClass? anyClass = decoder.DecodeAnyClass();

        // Assert
        Assert.That(anyClass, Is.Not.Null);
        Assert.That(anyClass, Is.InstanceOf<MyDerivedClass>());
        var myDerivedClass = (MyDerivedClass)anyClass;
        Assert.That(myDerivedClass, Is.Not.Null);
        Assert.That(myDerivedClass.UnknownSlices, Is.Not.Empty);
        Assert.That(myDerivedClass.M1, Is.EqualTo(p1.M1));
        Assert.That(myDerivedClass.M2, Is.EqualTo(p1.M2));
    }

    [Test]
    public void Slicing_preserve_unknown_slices()
    {
        // Arrange
        var buffer = new MemoryBufferWriter(new byte[1024 * 1024]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice1, classFormat: ClassFormat.Sliced);

        var p2 = new MyPreservedDerivedClass1("p2-m1", "p2-m2", new MyBaseClass("base"));
        var p1 = new MyPreservedDerivedClass1("p1-m1", "p1-m2", p2);
        encoder.EncodeClass(p1);

        // Create an activator that exclude 'MyPreservedDerivedClass1' type ID and ensure that the class is sliced and
        // the Slices are preserved.
        var slicingActivator = new SlicingActivator(
            SliceDecoder.GetActivator(typeof(MyPreservedDerivedClass1).Assembly),
            slicedTypeIds: ImmutableList.Create(MyPreservedDerivedClass1.SliceTypeId));

        var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice1, activator: slicingActivator);
        AnyClass? r1 = decoder.DecodeAnyClass();

        // Marshal the sliced class
        buffer = new MemoryBufferWriter(new byte[1024 * 1024]);
        encoder = new SliceEncoder(buffer, SliceEncoding.Slice1, classFormat: ClassFormat.Sliced);

        // Act
        encoder.EncodeClass(r1!);

        // Assert
        Assert.That(r1, Is.TypeOf<MyPreservedClass>());
        Assert.That(r1.UnknownSlices, Is.Not.Empty);
        // unmarshal again using the default factory, the unmarshaled class should contain the preserved Slices.
        decoder = new SliceDecoder(
            buffer.WrittenMemory,
            SliceEncoding.Slice1,
            activator: SliceDecoder.GetActivator(typeof(MyPreservedDerivedClass1).Assembly));

        MyPreservedDerivedClass1 r2 = decoder.DecodeClass<MyPreservedDerivedClass1>();
        Assert.That(r2.UnknownSlices, Is.Empty);
        Assert.That(r2.M1, Is.EqualTo("p1-m1"));
        Assert.That(r2.M2, Is.EqualTo("p1-m2"));
        Assert.That(r2.M3, Is.InstanceOf<MyPreservedDerivedClass1>());
        var r3 = (MyPreservedDerivedClass1)r2.M3;
        Assert.That(r3.M1, Is.EqualTo("p2-m1"));
        Assert.That(r3.M2, Is.EqualTo("p2-m2"));
        Assert.That(r3.M3.M1, Is.EqualTo("base"));
    }

    /// <summary>An activator that delegates to another activator except for the Sliced type IDs, for which it
    /// returns null instances. This allows testing class and exception slicing.</summary>
    private class SlicingActivator : IActivator
    {
        private readonly IActivator _decoratee;
        private readonly ImmutableList<string> _slicedTypeIds;

        public SlicingActivator(IActivator activator, ImmutableList<string>? slicedTypeIds = null)
        {
            _decoratee = activator;
            _slicedTypeIds = slicedTypeIds ?? ImmutableList<string>.Empty;
        }

        object? IActivator.CreateInstance(string typeId, ref SliceDecoder decoder) =>
            _slicedTypeIds.Contains(typeId) ? null : _decoratee.CreateInstance(typeId, ref decoder);
    }
}
