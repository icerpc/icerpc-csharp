// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using NUnit.Framework;
using System.Collections.Immutable;
using System.Reflection;

namespace IceRpc.Slice.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class SlicingTests
{
    /// <summary>An activator that delegates to another activator except for the Sliced type IDs, for which it
    /// returns null instances. This allows testing class and exception slicing.</summary>
    class SlicingActivator : IActivator
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

    [Test]
    public void Slicing_Classes()
    {
        Memory<byte> buffer = new byte[1024 * 1024];
        var bufferWriter = new MemoryBufferWriter(buffer);
        var encoder = new SliceEncoder(bufferWriter, Encoding.Slice11, classFormat: FormatType.Sliced);

        var p1 = new MyMostDerivedClass("most-derived", "derived", "base");
        encoder.EncodeClass(p1);
        buffer = bufferWriter.WrittenMemory;

        // Create an activator that knows about all the types using in this test
        var activator = SliceDecoder.GetActivator(new Assembly[]
        {
            typeof(MyMostDerivedClass).Assembly,
            typeof(MyDerivedClass).Assembly,
            typeof(MyBaseClass).Assembly
        });

        // First we unmarshal the class using the factory that know all the types, no Slicing should occur in this case.
        var decoder = new SliceDecoder(buffer, Encoding.Slice11, activator: activator);
        MyMostDerivedClass r = decoder.DecodeClass<MyMostDerivedClass>();
        Assert.That(p1.M1, Is.EqualTo(r.M1));
        Assert.That(p1.M2, Is.EqualTo(r.M2));
        Assert.That(p1.M3, Is.EqualTo(r.M3));

        // Create an activator that exclude 'MyMostDerivedClass' type ID and ensure that the class is unmarshaled as
        // 'MyDerivedClass' which is the base type.
        var slicingActivator = new SlicingActivator(
            activator,
            slicedTypeIds: ImmutableList.Create(MyMostDerivedClass.SliceTypeId));

        Assert.That(() =>
        {
            var decoder = new SliceDecoder(buffer, Encoding.Slice11, activator: slicingActivator);
            decoder.DecodeClass<MyMostDerivedClass>();
        },
        Throws.TypeOf<InvalidDataException>());

        decoder = new SliceDecoder(buffer, Encoding.Slice11, activator: slicingActivator);
        MyDerivedClass r1 = decoder.DecodeClass<MyDerivedClass>();
        Assert.That(r1.UnknownSlices, Is.Empty);
        Assert.That(p1.M1, Is.EqualTo(r1.M1));
        Assert.That(p1.M2, Is.EqualTo(r1.M2));

        // Repeat with an activator that also excludes 'MyDerivedClass' type ID
        slicingActivator = new SlicingActivator(
            activator,
            slicedTypeIds: ImmutableList.Create(MyMostDerivedClass.SliceTypeId, MyDerivedClass.SliceTypeId));

        Assert.That(() =>
        {
            var decoder = new SliceDecoder(buffer, Encoding.Slice11, activator: slicingActivator);
            decoder.DecodeClass<MyDerivedClass>();
        },
        Throws.TypeOf<InvalidDataException>());

        decoder = new SliceDecoder(buffer, Encoding.Slice11, activator: slicingActivator);
        MyBaseClass r2 = decoder.DecodeClass<MyBaseClass>();
        Assert.That(r2.UnknownSlices, Is.Empty);
        Assert.That(p1.M1, Is.EqualTo(r2.M1));

        // Repeat with an activator that also excludes 'MyBaseClass' type ID
        slicingActivator = new SlicingActivator(
                activator,
                slicedTypeIds: ImmutableList.Create(
                    MyMostDerivedClass.SliceTypeId,
                    MyDerivedClass.SliceTypeId,
                    MyBaseClass.SliceTypeId));

        Assert.That(() =>
        {
            var decoder = new SliceDecoder(buffer, Encoding.Slice11, activator: slicingActivator);
            decoder.DecodeClass<MyBaseClass>();
        },
        Throws.TypeOf<InvalidDataException>());

        Assert.That(() =>
        {
            var decoder = new SliceDecoder(buffer, Encoding.Slice11, activator: slicingActivator);
            decoder.DecodeClass<AnyClass>();
        },
        Throws.Nothing);
    }

    [Test]
    public void Slicing_Classes_WithCompactTypeId()
    {
        Memory<byte> buffer = new byte[1024 * 1024];
        var bufferWriter = new MemoryBufferWriter(buffer);
        var encoder = new SliceEncoder(bufferWriter, Encoding.Slice11, classFormat: FormatType.Sliced);

        var p1 = new MyCompactMostDerivedClass("most-derived", "derived", "base");
        encoder.EncodeClass(p1);
        buffer = bufferWriter.WrittenMemory;

        // Create an activator that knows about all the types using in this test
        var activator = SliceDecoder.GetActivator(new Assembly[]
        {
            typeof(MyCompactMostDerivedClass).Assembly,
            typeof(MyCompactDerivedClass).Assembly,
            typeof(MyCompactBaseClass).Assembly
        });

        // First we unmarshal the class using the default factories, no Slicing should occur in this case.
        var decoder = new SliceDecoder(buffer, Encoding.Slice11, activator: activator);
        MyCompactMostDerivedClass r = decoder.DecodeClass<MyCompactMostDerivedClass>();
        Assert.That(p1.M1, Is.EqualTo(r.M1));
        Assert.That(p1.M2, Is.EqualTo(r.M2));
        Assert.That(p1.M3, Is.EqualTo(r.M3));

        // Create an activator that exclude 'MyCompactMostDerivedClass' compact type ID (3) and ensure that
        // the class is unmarshaled as 'MyCompactDerivedClass' which is the base type.
        var slicingActivator = new SlicingActivator(
            activator,
            slicedTypeIds: ImmutableList.Create("3"));

        Assert.That(() =>
        {
            var decoder = new SliceDecoder(buffer, Encoding.Slice11, activator: slicingActivator);
            decoder.DecodeClass<MyCompactMostDerivedClass>();
        },
        Throws.TypeOf<InvalidDataException>());

        decoder = new SliceDecoder(buffer, Encoding.Slice11, activator: slicingActivator);
        MyCompactDerivedClass r1 = decoder.DecodeClass<MyCompactDerivedClass>();
        Assert.That(r1.UnknownSlices, Is.Empty);
        Assert.That(p1.M1, Is.EqualTo(r1.M1));
        Assert.That(p1.M2, Is.EqualTo(r1.M2));

        // Repeat with an activator that also excludes 'MyCompactDerivedClass' compact type ID (2)
        slicingActivator = slicingActivator = new SlicingActivator(
            activator,
            slicedTypeIds: ImmutableList.Create("3", "2"));

        Assert.That(() =>
        {
            var decoder = new SliceDecoder(buffer, Encoding.Slice11, activator: slicingActivator);
            decoder.DecodeClass<MyCompactDerivedClass>();
        },
        Throws.TypeOf<InvalidDataException>());

        decoder = new SliceDecoder(buffer, Encoding.Slice11, activator: slicingActivator);
        MyCompactBaseClass r2 = decoder.DecodeClass<MyCompactBaseClass>();
        Assert.That(r2.UnknownSlices, Is.Empty);
        Assert.That(p1.M1, Is.EqualTo(r2.M1));

        // Repeat with an activator that also excludes 'MyCompactBaseClass' compact type ID (1)
        slicingActivator = slicingActivator = new SlicingActivator(
            activator,
            slicedTypeIds: ImmutableList.Create("3", "2", "1"));

        Assert.That(() =>
        {
            var decoder = new SliceDecoder(buffer, Encoding.Slice11, activator: slicingActivator);
            decoder.DecodeClass<MyCompactBaseClass>();
        },
        Throws.TypeOf<InvalidDataException>());

        Assert.That(() =>
        {
            var decoder = new SliceDecoder(buffer, Encoding.Slice11, activator: slicingActivator);
            decoder.DecodeClass<AnyClass>();
        },
        Throws.Nothing);
    }

    [Test]
    public void Slicing_Exceptions()
    {
        Memory<byte> buffer = new byte[1024 * 1024];
        var bufferWriter = new MemoryBufferWriter(buffer);
        var encoder = new SliceEncoder(bufferWriter, Encoding.Slice11, classFormat: FormatType.Sliced);

        var p1 = new MyMostDerivedException("most-derived", "derived", "base");
        p1.EncodeTrait(ref encoder);
        buffer = bufferWriter.WrittenMemory;

        // Create an activator that knows about all the types using in this test
        var activator = SliceDecoder.GetActivator(new Assembly[]
        {
            typeof(MyMostDerivedException).Assembly,
            typeof(MyMostDerivedException).Assembly,
            typeof(MyBaseException).Assembly
        });

        // First we unmarshal the exception using the factory that knows all the types, no Slicing should occur in this case.
        var decoder = new SliceDecoder(buffer, Encoding.Slice11, activator: activator);
        RemoteException r = decoder.DecodeException((ResultType)SliceResultType.ServiceFailure);
        Assert.That(r, Is.InstanceOf<MyMostDerivedException>());
        var r1 = (MyMostDerivedException)r;
        Assert.That(p1.M1, Is.EqualTo(r1.M1));
        Assert.That(p1.M2, Is.EqualTo(r1.M2));
        Assert.That(p1.M3, Is.EqualTo(r1.M3));

        // Create an activator that exclude 'MyMostDerivedException' type ID and ensure that the class is unmarshaled
        // as 'MyDerivedException' which is the base type.
        var slicingActivator = new SlicingActivator(
            activator,
            slicedTypeIds: ImmutableList.Create("::IceRpc::Slice::Tests::MyMostDerivedException"));

        decoder = new SliceDecoder(buffer, Encoding.Slice11, activator: slicingActivator);

        r = decoder.DecodeException((ResultType)SliceResultType.ServiceFailure);
        Assert.That(r, Is.InstanceOf<MyDerivedException>());
        Assert.That(r, Is.Not.InstanceOf<MyMostDerivedException>());
        var r2 = (MyDerivedException)r;
        Assert.That(p1.M1, Is.EqualTo(r2.M1));
        Assert.That(p1.M2, Is.EqualTo(r2.M2));

        // Repeat with an activator that also excludes 'MyDerivedException' type ID
        slicingActivator = new SlicingActivator(
            activator,
            slicedTypeIds: ImmutableList.Create(
                "::IceRpc::Slice::Tests::MyMostDerivedException",
                "::IceRpc::Slice::Tests::MyDerivedException"));

        decoder = new SliceDecoder(buffer, Encoding.Slice11, activator: slicingActivator);
        r = decoder.DecodeException((ResultType)SliceResultType.ServiceFailure);
        Assert.That(r, Is.Not.InstanceOf<MyDerivedException>());
        Assert.That(r, Is.InstanceOf<MyBaseException>());
        var r3 = (MyBaseException)r;
        Assert.That(p1.M1, Is.EqualTo(r3.M1));

        // Repeat with an activator that also excludes 'MyBaseException' type ID
        slicingActivator = new SlicingActivator(
            activator,
            slicedTypeIds: ImmutableList.Create(
                "::IceRpc::Slice::Tests::MyMostDerivedException",
                "::IceRpc::Slice::Tests::MyDerivedException",
                "::IceRpc::Slice::Tests::MyBaseException"));

        decoder = new SliceDecoder(buffer, Encoding.Slice11, activator: slicingActivator);
        r = decoder.DecodeException((ResultType)SliceResultType.ServiceFailure);
        Assert.That(r, Is.Not.InstanceOf<MyBaseException>());
        Assert.That(r, Is.InstanceOf<UnknownException>());
        Assert.That(((UnknownException)r).TypeId, Is.EqualTo("::IceRpc::Slice::Tests::MyMostDerivedException"));
    }

    [Test]
    public void Slicing_PreservedClasses()
    {
        Memory<byte> buffer = new byte[1024 * 1024];
        var bufferWriter = new MemoryBufferWriter(buffer);
        var encoder = new SliceEncoder(bufferWriter, Encoding.Slice11, classFormat: FormatType.Sliced);

        var p2 = new MyPreservedDerivedClass1("p2-m1", "p2-m2", new MyBaseClass("base"));
        var p1 = new MyPreservedDerivedClass1("p1-m1", "p1-m2", p2);
        encoder.EncodeClass(p1);
        buffer = bufferWriter.WrittenMemory;

        // Create an activator that knows about all the types using in this test
        var activator = SliceDecoder.GetActivator(typeof(MyPreservedDerivedClass1).Assembly);

        // Create an activator that exclude 'MyPreservedDerivedClass1' type ID and ensure that the class is sliced and
        // the Slices are preserved.
        var slicingActivator = new SlicingActivator(
            activator,
            slicedTypeIds: ImmutableList.Create(MyPreservedDerivedClass1.SliceTypeId));

        Assert.That(() =>
        {
            var decoder = new SliceDecoder(buffer, Encoding.Slice11, activator: slicingActivator);
            decoder.DecodeClass<MyPreservedDerivedClass1>();
        },
        Throws.TypeOf<InvalidDataException>());

        var decoder = new SliceDecoder(buffer, Encoding.Slice11, activator: slicingActivator);
        MyBaseClass r1 = decoder.DecodeClass<MyBaseClass>();
        Assert.That(r1.UnknownSlices, Is.Not.Empty);

        // Marshal the sliced class
        buffer = new byte[1024 * 1024];
        bufferWriter = new MemoryBufferWriter(buffer);
        encoder = new SliceEncoder(bufferWriter, Encoding.Slice11, classFormat: FormatType.Sliced);
        encoder.EncodeClass(r1);
        buffer = bufferWriter.WrittenMemory;

        // unmarshal again using the default factory, the unmarshaled class should contain the preserved Slices.
        decoder = new SliceDecoder(buffer, Encoding.Slice11, activator: activator);
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

    [Test]
    public void Slicing_PreservedClasses_WithCompactTypeId()
    {
        Memory<byte> buffer = new byte[1024 * 1024];
        var bufferWriter = new MemoryBufferWriter(buffer);
        var encoder = new SliceEncoder(bufferWriter, Encoding.Slice11, classFormat: FormatType.Sliced);

        var p2 = new MyPreservedDerivedClass2("p2-m1", "p2-m2", new MyBaseClass("base"));
        var p1 = new MyPreservedDerivedClass2("p1-m1", "p1-m2", p2);
        encoder.EncodeClass(p1);
        buffer = bufferWriter.WrittenMemory;

        // Create an activator that knows about all the types using in this test
        var activator = SliceDecoder.GetActivator(typeof(MyPreservedDerivedClass2).Assembly);

        // Create an activator that exclude 'MyPreservedDerivedClass2' compact type ID (56) and ensure that the class
        // is sliced and the Slices are preserved.
        var slicingActivator = new SlicingActivator(
            activator,
            slicedTypeIds: ImmutableList.Create("56"));

        Assert.That(() =>
        {
            var decoder = new SliceDecoder(buffer, Encoding.Slice11, activator: slicingActivator);
            decoder.DecodeClass<MyPreservedDerivedClass2>();
        },
        Throws.TypeOf<InvalidDataException>());

        var decoder = new SliceDecoder(buffer, Encoding.Slice11, activator: slicingActivator);
        MyBaseClass r1 = decoder.DecodeClass<MyBaseClass>();

        // Marshal the sliced class
        buffer = new byte[1024 * 1024];
        bufferWriter = new MemoryBufferWriter(buffer);
        encoder = new SliceEncoder(bufferWriter, Encoding.Slice11, classFormat: FormatType.Sliced);
        encoder.EncodeClass(r1);
        buffer = bufferWriter.WrittenMemory;

        // unmarshal again using the default factory, the unmarshaled class should contain the preserved Slices.
        decoder = new SliceDecoder(buffer, Encoding.Slice11, activator: activator);
        MyPreservedDerivedClass2 r2 = decoder.DecodeClass<MyPreservedDerivedClass2>();
        Assert.That(r2.M1, Is.EqualTo("p1-m1"));
        Assert.That(r2.M2, Is.EqualTo("p1-m2"));
        Assert.That(r2.M3, Is.InstanceOf<MyPreservedDerivedClass2>());
        var r3 = (MyPreservedDerivedClass2)r2.M3;
        Assert.That(r3.M1, Is.EqualTo("p2-m1"));
        Assert.That(r3.M2, Is.EqualTo("p2-m2"));
        Assert.That(r3.M3.M1, Is.EqualTo("base"));
    }
}
