// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Slice.Internal;
using NUnit.Framework;
using System.Collections.Immutable;
using System.Reflection;

namespace IceRpc.Tests.SliceInternal
{
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

            object? IActivator.CreateInstance(string typeId, ref IceDecoder decoder) =>
                _slicedTypeIds.Contains(typeId) ? null : _decoratee.CreateInstance(typeId, ref decoder);
        }

        [Test]
        public void Slicing_Classes()
        {
            Memory<byte> buffer = new byte[1024 * 1024];
            var bufferWriter = new SingleBufferWriter(buffer);
            var encoder = new IceEncoder(bufferWriter, Encoding.Ice11, classFormat: FormatType.Sliced);

            var p1 = new MyMostDerivedClass("most-derived", "derived", "base");
            encoder.EncodeClass(p1);
            buffer = bufferWriter.WrittenBuffer;

            // Create an activator that knows about all the types using in this test
            var activator = IceDecoder.GetActivator(new Assembly[]
            {
                typeof(MyMostDerivedClass).Assembly,
                typeof(MyDerivedClass).Assembly,
                typeof(MyBaseClass).Assembly
            });

            // First we unmarshal the class using the factory that know all the types, no Slicing should occur in this case.
            var decoder = new IceDecoder(buffer, Encoding.Ice11, activator: activator);
            MyMostDerivedClass r = decoder.DecodeClass<MyMostDerivedClass>();
            Assert.AreEqual(p1.M1, r.M1);
            Assert.AreEqual(p1.M2, r.M2);
            Assert.AreEqual(p1.M3, r.M3);

            // Create an activator that exclude 'MyMostDerivedClass' type ID and ensure that the class is unmarshaled as
            // 'MyDerivedClass' which is the base type.
            var slicingActivator = new SlicingActivator(
                activator,
                slicedTypeIds: ImmutableList.Create(MyMostDerivedClass.IceTypeId));

            Assert.Throws<InvalidDataException>(() =>
            {
                var decoder = new IceDecoder(buffer, Encoding.Ice11, activator: slicingActivator);
                decoder.DecodeClass<MyMostDerivedClass>();
            });

            decoder = new IceDecoder(buffer, Encoding.Ice11, activator: slicingActivator);
            MyDerivedClass r1 = decoder.DecodeClass<MyDerivedClass>();
            Assert.That(r1.UnknownSlices, Is.Empty);
            Assert.AreEqual(p1.M1, r1.M1);
            Assert.AreEqual(p1.M2, r1.M2);

            // Repeat with an activator that also excludes 'MyDerivedClass' type ID
            slicingActivator = new SlicingActivator(
                activator,
                slicedTypeIds: ImmutableList.Create(MyMostDerivedClass.IceTypeId, MyDerivedClass.IceTypeId));

            Assert.Throws<InvalidDataException>(() =>
            {
                var decoder = new IceDecoder(buffer, Encoding.Ice11, activator: slicingActivator);
                decoder.DecodeClass<MyDerivedClass>();
            });

            decoder = new IceDecoder(buffer, Encoding.Ice11, activator: slicingActivator);
            MyBaseClass r2 = decoder.DecodeClass<MyBaseClass>();
            Assert.That(r2.UnknownSlices, Is.Empty);
            Assert.AreEqual(p1.M1, r2.M1);

            // Repeat with an activator that also excludes 'MyBaseClass' type ID
            slicingActivator = new SlicingActivator(
                    activator,
                    slicedTypeIds: ImmutableList.Create(
                        MyMostDerivedClass.IceTypeId,
                        MyDerivedClass.IceTypeId,
                        MyBaseClass.IceTypeId));

            Assert.Throws<InvalidDataException>(() =>
            {
                var decoder = new IceDecoder(buffer, Encoding.Ice11, activator: slicingActivator);
                decoder.DecodeClass<MyBaseClass>();
            });

            Assert.DoesNotThrow(() =>
            {
                var decoder = new IceDecoder(buffer, Encoding.Ice11, activator: slicingActivator);
                decoder.DecodeClass<AnyClass>();
            });
        }

        [Test]
        public void Slicing_Classes_WithCompactTypeId()
        {
            Memory<byte> buffer = new byte[1024 * 1024];
            var bufferWriter = new SingleBufferWriter(buffer);
            var encoder = new IceEncoder(bufferWriter, Encoding.Ice11, classFormat: FormatType.Sliced);

            var p1 = new MyCompactMostDerivedClass("most-derived", "derived", "base");
            encoder.EncodeClass(p1);
            buffer = bufferWriter.WrittenBuffer;

            // Create an activator that knows about all the types using in this test
            var activator = IceDecoder.GetActivator(new Assembly[]
            {
                typeof(MyCompactMostDerivedClass).Assembly,
                typeof(MyCompactDerivedClass).Assembly,
                typeof(MyCompactBaseClass).Assembly
            });

            // First we unmarshal the class using the default factories, no Slicing should occur in this case.
            var decoder = new IceDecoder(buffer, Encoding.Ice11, activator: activator);
            MyCompactMostDerivedClass r = decoder.DecodeClass<MyCompactMostDerivedClass>();
            Assert.AreEqual(p1.M1, r.M1);
            Assert.AreEqual(p1.M2, r.M2);
            Assert.AreEqual(p1.M3, r.M3);

            // Create an activator that exclude 'MyCompactMostDerivedClass' compact type ID (3) and ensure that
            // the class is unmarshaled as 'MyCompactDerivedClass' which is the base type.
            var slicingActivator = new SlicingActivator(
                activator,
                slicedTypeIds: ImmutableList.Create("3"));

            Assert.Throws<InvalidDataException>(() =>
            {
                var decoder = new IceDecoder(buffer, Encoding.Ice11, activator: slicingActivator);
                decoder.DecodeClass<MyCompactMostDerivedClass>();
            });

            decoder = new IceDecoder(buffer, Encoding.Ice11, activator: slicingActivator);
            MyCompactDerivedClass r1 = decoder.DecodeClass<MyCompactDerivedClass>();
            Assert.That(r1.UnknownSlices, Is.Empty);
            Assert.AreEqual(p1.M1, r1.M1);
            Assert.AreEqual(p1.M2, r1.M2);

            // Repeat with an activator that also excludes 'MyCompactDerivedClass' compact type ID (2)
            slicingActivator = slicingActivator = new SlicingActivator(
                activator,
                slicedTypeIds: ImmutableList.Create("3", "2"));

            Assert.Throws<InvalidDataException>(() =>
            {
                var decoder = new IceDecoder(buffer, Encoding.Ice11, activator: slicingActivator);
                decoder.DecodeClass<MyCompactDerivedClass>();
            });

            decoder = new IceDecoder(buffer, Encoding.Ice11, activator: slicingActivator);
            MyCompactBaseClass r2 = decoder.DecodeClass<MyCompactBaseClass>();
            Assert.That(r2.UnknownSlices, Is.Empty);
            Assert.AreEqual(p1.M1, r2.M1);

            // Repeat with an activator that also excludes 'MyCompactBaseClass' compact type ID (1)
            slicingActivator = slicingActivator = new SlicingActivator(
                activator,
                slicedTypeIds: ImmutableList.Create("3", "2", "1"));

            Assert.Throws<InvalidDataException>(() =>
            {
                var decoder = new IceDecoder(buffer, Encoding.Ice11, activator: slicingActivator);
                decoder.DecodeClass<MyCompactBaseClass>();
            });

            Assert.DoesNotThrow(() =>
            {
                var decoder = new IceDecoder(buffer, Encoding.Ice11, activator: slicingActivator);
                decoder.DecodeClass<AnyClass>();
            });
        }

        [Test]
        public void Slicing_Exceptions()
        {
            Memory<byte> buffer = new byte[1024 * 1024];
            var bufferWriter = new SingleBufferWriter(buffer);
            var encoder = new IceEncoder(bufferWriter, Encoding.Ice11, classFormat: FormatType.Sliced);

            var p1 = new MyMostDerivedException("most-derived", "derived", "base");
            encoder.EncodeException(p1);
            buffer = bufferWriter.WrittenBuffer;

            // Create an activator that knows about all the types using in this test
            var activator = IceDecoder.GetActivator(new Assembly[]
            {
                typeof(MyMostDerivedException).Assembly,
                typeof(MyMostDerivedException).Assembly,
                typeof(MyBaseException).Assembly
            });

            // First we unmarshal the exception using the factory that knows all the types, no Slicing should occur in this case.
            var decoder = new IceDecoder(buffer, Encoding.Ice11, activator: activator);
            RemoteException r = decoder.DecodeException();
            Assert.That(r, Is.InstanceOf<MyMostDerivedException>());
            var r1 = (MyMostDerivedException)r;
            Assert.AreEqual(p1.M1, r1.M1);
            Assert.AreEqual(p1.M2, r1.M2);
            Assert.AreEqual(p1.M3, r1.M3);

            // Create an activator that exclude 'MyMostDerivedException' type ID and ensure that the class is unmarshaled
            // as 'MyDerivedException' which is the base type.
            var slicingActivator = new SlicingActivator(
                activator,
                slicedTypeIds: ImmutableList.Create("::IceRpc::Tests::SliceInternal::MyMostDerivedException"));

            decoder = new IceDecoder(buffer, Encoding.Ice11, activator: slicingActivator);

            r = decoder.DecodeException();
            Assert.That(r, Is.InstanceOf<MyDerivedException>());
            Assert.That(r, Is.Not.InstanceOf<MyMostDerivedException>());
            var r2 = (MyDerivedException)r;
            Assert.AreEqual(p1.M1, r2.M1);
            Assert.AreEqual(p1.M2, r2.M2);

            // Repeat with an activator that also excludes 'MyDerivedException' type ID
            slicingActivator = new SlicingActivator(
                activator,
                slicedTypeIds: ImmutableList.Create(
                    "::IceRpc::Tests::SliceInternal::MyMostDerivedException",
                    "::IceRpc::Tests::SliceInternal::MyDerivedException"));

            decoder = new IceDecoder(buffer, Encoding.Ice11, activator: slicingActivator);
            r = decoder.DecodeException();
            Assert.That(r, Is.Not.InstanceOf<MyDerivedException>());
            Assert.That(r, Is.InstanceOf<MyBaseException>());
            var r3 = (MyBaseException)r;
            Assert.AreEqual(p1.M1, r3.M1);

            // Repeat with an activator that also excludes 'MyBaseException' type ID
            slicingActivator = new SlicingActivator(
                activator,
                slicedTypeIds: ImmutableList.Create(
                    "::IceRpc::Tests::SliceInternal::MyMostDerivedException",
                    "::IceRpc::Tests::SliceInternal::MyDerivedException",
                    "::IceRpc::Tests::SliceInternal::MyBaseException"));

            decoder = new IceDecoder(buffer, Encoding.Ice11, activator: slicingActivator);
            r = decoder.DecodeException();
            Assert.That(r, Is.Not.InstanceOf<MyBaseException>());
            Assert.That(r, Is.InstanceOf<UnknownSlicedRemoteException>());
            Assert.AreEqual("::IceRpc::Tests::SliceInternal::MyMostDerivedException",
                            ((UnknownSlicedRemoteException)r).TypeId);

            // Marshal the exception again -- there is no Slice preservation for exceptions
            buffer = new byte[1024 * 1024];
            bufferWriter = new SingleBufferWriter(buffer);
            encoder = new IceEncoder(bufferWriter, Encoding.Ice11, classFormat: FormatType.Sliced);
            encoder.EncodeException(r);
            buffer = bufferWriter.WrittenBuffer;

            decoder = new IceDecoder(buffer, Encoding.Ice11, activator: activator);
            r = decoder.DecodeException();
            Assert.That(r, Is.Not.InstanceOf<UnknownSlicedRemoteException>());
            Assert.That(r, Is.InstanceOf<RemoteException>()); // a plain RemoteException
        }

        [Test]
        public void Slicing_PreservedClasses()
        {
            Memory<byte> buffer = new byte[1024 * 1024];
            var bufferWriter = new SingleBufferWriter(buffer);
            var encoder = new IceEncoder(bufferWriter, Encoding.Ice11, classFormat: FormatType.Sliced);

            var p2 = new MyPreservedDerivedClass1("p2-m1", "p2-m2", new MyBaseClass("base"));
            var p1 = new MyPreservedDerivedClass1("p1-m1", "p1-m2", p2);
            encoder.EncodeClass(p1);
            buffer = bufferWriter.WrittenBuffer;

            // Create an activator that knows about all the types using in this test
            var activator = IceDecoder.GetActivator(typeof(MyPreservedDerivedClass1).Assembly);

            // Create an activator that exclude 'MyPreservedDerivedClass1' type ID and ensure that the class is sliced and
            // the Slices are preserved.
            var slicingActivator = new SlicingActivator(
                activator,
                slicedTypeIds: ImmutableList.Create(MyPreservedDerivedClass1.IceTypeId));

            Assert.Throws<InvalidDataException>(() =>
            {
                var decoder = new IceDecoder(buffer, Encoding.Ice11, activator: slicingActivator);
                decoder.DecodeClass<MyPreservedDerivedClass1>();
            });

            var decoder = new IceDecoder(buffer, Encoding.Ice11, activator: slicingActivator);
            MyBaseClass r1 = decoder.DecodeClass<MyBaseClass>();
            Assert.That(r1.UnknownSlices, Is.Not.Empty);

            // Marshal the sliced class
            buffer = new byte[1024 * 1024];
            bufferWriter = new SingleBufferWriter(buffer);
            encoder = new IceEncoder(bufferWriter, Encoding.Ice11, classFormat: FormatType.Sliced);
            encoder.EncodeClass(r1);
            buffer = bufferWriter.WrittenBuffer;

            // unmarshal again using the default factory, the unmarshaled class should contain the preserved Slices.
            decoder = new IceDecoder(buffer, Encoding.Ice11, activator: activator);
            MyPreservedDerivedClass1 r2 = decoder.DecodeClass<MyPreservedDerivedClass1>();
            Assert.That(r2.UnknownSlices, Is.Empty);
            Assert.AreEqual("p1-m1", r2.M1);
            Assert.AreEqual("p1-m2", r2.M2);
            Assert.That(r2.M3, Is.InstanceOf<MyPreservedDerivedClass1>());
            var r3 = (MyPreservedDerivedClass1)r2.M3;
            Assert.AreEqual("p2-m1", r3.M1);
            Assert.AreEqual("p2-m2", r3.M2);
            Assert.AreEqual("base", r3.M3.M1);
        }

        [Test]
        public void Slicing_PreservedClasses_WithCompactTypeId()
        {
            Memory<byte> buffer = new byte[1024 * 1024];
            var bufferWriter = new SingleBufferWriter(buffer);
            var encoder = new IceEncoder(bufferWriter, Encoding.Ice11, classFormat: FormatType.Sliced);

            var p2 = new MyPreservedDerivedClass2("p2-m1", "p2-m2", new MyBaseClass("base"));
            var p1 = new MyPreservedDerivedClass2("p1-m1", "p1-m2", p2);
            encoder.EncodeClass(p1);
            buffer = bufferWriter.WrittenBuffer;

            // Create an activator that knows about all the types using in this test
            var activator = IceDecoder.GetActivator(typeof(MyPreservedDerivedClass2).Assembly);

            // Create an activator that exclude 'MyPreservedDerivedClass2' compact type ID (56) and ensure that the class
            // is sliced and the Slices are preserved.
            var slicingActivator = new SlicingActivator(
                activator,
                slicedTypeIds: ImmutableList.Create("56"));

            Assert.Throws<InvalidDataException>(() =>
            {
                var decoder = new IceDecoder(buffer, Encoding.Ice11, activator: slicingActivator);
                decoder.DecodeClass<MyPreservedDerivedClass2>();
            });

            var decoder = new IceDecoder(buffer, Encoding.Ice11, activator: slicingActivator);
            MyBaseClass r1 = decoder.DecodeClass<MyBaseClass>();

            // Marshal the sliced class
            buffer = new byte[1024 * 1024];
            bufferWriter = new SingleBufferWriter(buffer);
            encoder = new IceEncoder(bufferWriter, Encoding.Ice11, classFormat: FormatType.Sliced);
            encoder.EncodeClass(r1);
            buffer = bufferWriter.WrittenBuffer;

            // unmarshal again using the default factory, the unmarshaled class should contain the preserved Slices.
            decoder = new IceDecoder(buffer, Encoding.Ice11, activator: activator);
            MyPreservedDerivedClass2 r2 = decoder.DecodeClass<MyPreservedDerivedClass2>();
            Assert.AreEqual("p1-m1", r2.M1);
            Assert.AreEqual("p1-m2", r2.M2);
            Assert.That(r2.M3, Is.InstanceOf<MyPreservedDerivedClass2>());
            var r3 = (MyPreservedDerivedClass2)r2.M3;
            Assert.AreEqual("p2-m1", r3.M1);
            Assert.AreEqual("p2-m2", r3.M2);
            Assert.AreEqual("base", r3.M3.M1);
        }
    }
}
