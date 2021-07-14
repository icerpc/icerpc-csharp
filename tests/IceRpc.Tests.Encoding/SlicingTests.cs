// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Collections.Immutable;

namespace IceRpc.Tests.Encoding
{
    [Parallelizable(scope: ParallelScope.All)]
    public class SlicingTests
    {
        /// <summary>A class factory that delegates to another factory except for the Sliced type IDs, for which it
        /// returns null instances. This allows testing class and exception slicing.</summary>
        class SlicingClassFactory : IClassFactory
        {
            private readonly IClassFactory _classFactory;
            private readonly ImmutableList<string> _slicedClassTypeIds;
            private readonly ImmutableList<int> _slicedClassCompactTypeIds;
            private readonly ImmutableList<string> _slicedExceptionTypeIds;

            public SlicingClassFactory(
                IClassFactory classFactory,
                ImmutableList<string>? slicedClassTypeIds = null,
                ImmutableList<int>? slicedClassCompactTypeIds = null,
                ImmutableList<string>? slicedExceptionTypeIds = null)
            {
                _classFactory = classFactory;
                _slicedClassTypeIds = slicedClassTypeIds ?? ImmutableList<string>.Empty;
                _slicedClassCompactTypeIds = slicedClassCompactTypeIds ?? ImmutableList<int>.Empty;
                _slicedExceptionTypeIds = slicedExceptionTypeIds ?? ImmutableList<string>.Empty;
            }

            public AnyClass? CreateClassInstance(string typeId) =>
                _slicedClassTypeIds.Contains(typeId) ? null : _classFactory.CreateClassInstance(typeId);
            public AnyClass? CreateClassInstance(int compactId) =>
                _slicedClassCompactTypeIds.Contains(compactId) ? null : _classFactory.CreateClassInstance(compactId);

            public RemoteException? CreateRemoteException(
                string typeId,
                string? message,
                RemoteExceptionOrigin origin) => 
                _slicedExceptionTypeIds.Contains(typeId) ?
                    null : _classFactory.CreateRemoteException(typeId, message, origin);
        }

        [TestCase((byte)1, (byte)1)]
        [TestCase((byte)2, (byte)0)]
        public void Slicing_Classes(byte encodingMajor, byte encodingMinor)
        {
            var encoding = new IceRpc.Encoding(encodingMajor, encodingMinor);
            byte[] buffer = new byte[1024 * 1024];
            var encoder = new IceEncoder(encoding, buffer, classFormat: FormatType.Sliced);

            var p1 = new MyMostDerivedClass("most-derived", "derived", "base");
            encoder.EncodeClass(p1, null);
            ReadOnlyMemory<byte> data = encoder.Finish().Span[0];

            // First we unmarshal the class using the default factories, no Slicing should occur in this case.
            var decoder = new IceDecoder(data, encoding);
            MyMostDerivedClass r = decoder.DecodeClass<MyMostDerivedClass>(null);
            Assert.AreEqual(p1.M1, r.M1);
            Assert.AreEqual(p1.M2, r.M2);
            Assert.AreEqual(p1.M3, r.M3);

            // Create a factory that exclude 'MyMostDerivedClass' type ID and ensure that the class is unmarshaled as
            // 'MyDerivedClass' which is the base type.
            var classFactory = new SlicingClassFactory(
                ClassFactory.Default,
                slicedClassTypeIds: ImmutableList.Create(MyMostDerivedClass.IceTypeId));

            decoder = new IceDecoder(data, encoding, classFactory: classFactory);
            Assert.Throws<InvalidDataException>(() => decoder.DecodeClass<MyMostDerivedClass>(null));
            decoder = new IceDecoder(data, encoding, classFactory: classFactory);
            MyDerivedClass r1 = decoder.DecodeClass<MyDerivedClass>(null);
            Assert.That(r1.SlicedData, Is.Null);
            Assert.AreEqual(p1.M1, r1.M1);
            Assert.AreEqual(p1.M2, r1.M2);

            // Repeat with a factory that also excludes 'MyDerivedClass' type ID
            classFactory = new SlicingClassFactory(
                ClassFactory.Default,
                slicedClassTypeIds: ImmutableList.Create(MyMostDerivedClass.IceTypeId, MyDerivedClass.IceTypeId));

            decoder = new IceDecoder(data, encoding, classFactory: classFactory);
            Assert.Throws<InvalidDataException>(() => decoder.DecodeClass<MyDerivedClass>(null));
            decoder = new IceDecoder(data, encoding, classFactory: classFactory);
            MyBaseClass r2 = decoder.DecodeClass<MyBaseClass>(null);
            Assert.That(r2.SlicedData, Is.Null);
            Assert.AreEqual(p1.M1, r2.M1);

            // Repeat with a factory that also excludes 'MyBaseClass' type ID
            classFactory = new SlicingClassFactory(
                    ClassFactory.Default,
                    slicedClassTypeIds: ImmutableList.Create(
                        MyMostDerivedClass.IceTypeId, 
                        MyDerivedClass.IceTypeId,
                        MyBaseClass.IceTypeId));

            decoder = new IceDecoder(data, encoding, classFactory: classFactory);
            Assert.Throws<InvalidDataException>(() => decoder.DecodeClass<MyBaseClass>(null));
            decoder = new IceDecoder(data, encoding, classFactory: classFactory);
            Assert.DoesNotThrow(() => decoder.DecodeClass<AnyClass>(null));
        }

        [TestCase((byte)1, (byte)1)]
        public void Slicing_Classes_WithCompactTypeId(byte encodingMajor, byte encodingMinor)
        {
            var encoding = new IceRpc.Encoding(encodingMajor, encodingMinor);
            byte[] buffer = new byte[1024 * 1024];
            var encoder = new IceEncoder(encoding, buffer, classFormat: FormatType.Sliced);

            var p1 = new MyCompactMostDerivedClass("most-derived", "derived", "base");
            encoder.EncodeClass(p1, null);
            ReadOnlyMemory<byte> data = encoder.Finish().Span[0];

            // First we unmarshal the class using the default factories, no Slicing should occur in this case.
            var decoder = new IceDecoder(data, encoding);
            MyCompactMostDerivedClass r = decoder.DecodeClass<MyCompactMostDerivedClass>(null);
            Assert.AreEqual(p1.M1, r.M1);
            Assert.AreEqual(p1.M2, r.M2);
            Assert.AreEqual(p1.M3, r.M3);

            // Create a factory that exclude 'MyCompactMostDerivedClass' compact type ID (3) and ensure that
            // the class is unmarshaled as 'MyCompactDerivedClass' which is the base type.
            var classFactory = new SlicingClassFactory(
                ClassFactory.Default,
                slicedClassCompactTypeIds: ImmutableList.Create(3));
            decoder = new IceDecoder(data, encoding, classFactory: classFactory);
            Assert.Throws<InvalidDataException>(() => decoder.DecodeClass<MyCompactMostDerivedClass>(null));
            decoder = new IceDecoder(data, encoding, classFactory: classFactory);
            MyCompactDerivedClass r1 = decoder.DecodeClass<MyCompactDerivedClass>(null);
            Assert.That(r1.SlicedData, Is.Null);
            Assert.AreEqual(p1.M1, r1.M1);
            Assert.AreEqual(p1.M2, r1.M2);

            // Repeat with a factory that also excludes 'MyCompactDerivedClass' compact type ID (2)
            classFactory = classFactory = new SlicingClassFactory(
                ClassFactory.Default,
                slicedClassCompactTypeIds: ImmutableList.Create(3, 2));
            decoder = new IceDecoder(data, encoding, classFactory: classFactory);
            Assert.Throws<InvalidDataException>(() => decoder.DecodeClass<MyCompactDerivedClass>(null));
            decoder = new IceDecoder(data, encoding, classFactory: classFactory);
            MyCompactBaseClass r2 = decoder.DecodeClass<MyCompactBaseClass>(null);
            Assert.That(r2.SlicedData, Is.Null);
            Assert.AreEqual(p1.M1, r2.M1);

            // Repeat with a factory that also excludes 'MyCompactBaseClass' compact type ID (1)
            classFactory = classFactory = new SlicingClassFactory(
                ClassFactory.Default,
                slicedClassCompactTypeIds: ImmutableList.Create(3, 2, 1));
            decoder = new IceDecoder(data, encoding, classFactory: classFactory);
            Assert.Throws<InvalidDataException>(() => decoder.DecodeClass<MyCompactBaseClass>(null));
            decoder = new IceDecoder(data, encoding, classFactory: classFactory);
            Assert.DoesNotThrow(() => decoder.DecodeClass<AnyClass>(null));
        }

        [TestCase((byte)1, (byte)1)]
        [TestCase((byte)2, (byte)0)]
        public void Slicing_Exceptions(byte encodingMajor, byte encodingMinor)
        {
            var encoding = new IceRpc.Encoding(encodingMajor, encodingMinor);
            byte[] buffer = new byte[1024 * 1024];
            var encoder = new IceEncoder(encoding, buffer, classFormat: FormatType.Sliced);

            var p1 = new MyMostDerivedException("most-derived", "derived", "base");
            encoder.EncodeException(p1);
            ReadOnlyMemory<byte> data = encoder.Finish().Span[0];

            // First we unmarshal the exception using the default factories, no Slicing should occur in this case.
            var decoder = new IceDecoder(data, encoding);
            RemoteException r = decoder.DecodeException();
            Assert.That(r.SlicedData, Is.Null);
            Assert.That(r, Is.InstanceOf<MyMostDerivedException>());
            var r1 = (MyMostDerivedException)r;
            Assert.AreEqual(p1.M1, r1.M1);
            Assert.AreEqual(p1.M2, r1.M2);
            Assert.AreEqual(p1.M3, r1.M3);

            // Create a factory that exclude 'MyMostDerivedException' type ID and ensure that the class is unmarshaled
            // as 'MyDerivedException' which is the base type.
            var classFactory = new SlicingClassFactory(
                ClassFactory.Default,
                slicedExceptionTypeIds: ImmutableList.Create("::IceRpc::Tests::Encoding::MyMostDerivedException"));

            decoder = new IceDecoder(data, encoding, classFactory: classFactory);

            r = decoder.DecodeException();
            Assert.That(r.SlicedData, Is.Not.Null);
            Assert.That(r, Is.InstanceOf<MyDerivedException>());
            Assert.That(r, Is.Not.InstanceOf<MyMostDerivedException>());
            var r2 = (MyDerivedException)r;
            Assert.AreEqual(p1.M1, r2.M1);
            Assert.AreEqual(p1.M2, r2.M2);

            // Repeat with a factory that also excludes 'MyDerivedException' type ID
            classFactory = new SlicingClassFactory(
                ClassFactory.Default,
                slicedExceptionTypeIds: ImmutableList.Create(
                    "::IceRpc::Tests::Encoding::MyMostDerivedException",
                    "::IceRpc::Tests::Encoding::MyDerivedException"));

            decoder = new IceDecoder(data, encoding, classFactory: classFactory);
            r = decoder.DecodeException();
            Assert.That(r.SlicedData, Is.Not.Null);
            Assert.That(r, Is.Not.InstanceOf<MyDerivedException>());
            Assert.That(r, Is.InstanceOf<MyBaseException>());
            var r3 = (MyBaseException)r;
            Assert.AreEqual(p1.M1, r3.M1);

            // Repeat with a factory that also excludes 'MyBaseException' type ID
            classFactory = new SlicingClassFactory(
                ClassFactory.Default,
                slicedExceptionTypeIds: ImmutableList.Create(
                    "::IceRpc::Tests::Encoding::MyMostDerivedException",
                    "::IceRpc::Tests::Encoding::MyDerivedException",
                    "::IceRpc::Tests::Encoding::MyBaseException"));

            decoder = new IceDecoder(data, encoding, classFactory: classFactory);
            r = decoder.DecodeException();
            Assert.That(r.SlicedData, Is.Not.Null);
            Assert.That(r, Is.Not.InstanceOf<MyBaseException>());

            // Marshal the exception again to ensure all Slices are correctly preserved
            encoder = new IceEncoder(encoding, buffer, classFormat: FormatType.Sliced);
            encoder.EncodeException(r);
            data = encoder.Finish().Span[0];

            decoder = new IceDecoder(data, encoding);
            r = decoder.DecodeException();
            Assert.That(r.SlicedData, Is.Null);
            Assert.That(r, Is.InstanceOf<MyMostDerivedException>());
            r1 = (MyMostDerivedException)r;
            Assert.AreEqual(p1.M1, r1.M1);
            Assert.AreEqual(p1.M2, r1.M2);
            Assert.AreEqual(p1.M3, r1.M3);
        }

        [TestCase((byte)1, (byte)1)]
        [TestCase((byte)2, (byte)0)]
        public void Slicing_PreservedClasses(byte encodingMajor, byte encodingMinor)
        {
            var encoding = new IceRpc.Encoding(encodingMajor, encodingMinor);
            byte[] buffer = new byte[1024 * 1024];
            var encoder = new IceEncoder(encoding, buffer, classFormat: FormatType.Sliced);

            var p2 = new MyPreservedDerivedClass1("p2-m1", "p2-m2", new MyBaseClass("base"));
            var p1 = new MyPreservedDerivedClass1("p1-m1", "p1-m2", p2);
            encoder.EncodeClass(p1, null);
            ReadOnlyMemory<byte> data = encoder.Finish().Span[0];

            // Create a factory that exclude 'MyPreservedDerivedClass1' type ID and ensure that the class is sliced and
            // the Slices are preserved.
            var classFactory = new SlicingClassFactory(
                ClassFactory.Default,
                slicedExceptionTypeIds: ImmutableList.Create(MyPreservedDerivedClass1.IceTypeId));

            var decoder = new IceDecoder(data, encoding, classFactory: classFactory);
            Assert.Throws<InvalidDataException>(() => decoder.DecodeClass<MyPreservedDerivedClass1>(null));

            decoder = new IceDecoder(data, encoding, classFactory: classFactory);
            MyBaseClass r1 = decoder.DecodeClass<MyBaseClass>(null);
            Assert.That(r1.SlicedData, Is.Not.Null);

            // Marshal the sliced class
            buffer = new byte[1024 * 1024];
            encoder = new IceEncoder(encoding, buffer, classFormat: FormatType.Sliced);
            encoder.EncodeClass(r1, null);
            data = encoder.Finish().Span[0];

            // unmarshal again using the default factory, the unmarshaled class should contain the preserved Slices.
            decoder = new IceDecoder(data, encoding, classFactory: ClassFactory.Default);
            MyPreservedDerivedClass1 r2 = decoder.DecodeClass<MyPreservedDerivedClass1>(null);
            Assert.That(r2.SlicedData, Is.Null);
            Assert.AreEqual("p1-m1", r2.M1);
            Assert.AreEqual("p1-m2", r2.M2);
            Assert.That(r2.M3, Is.InstanceOf<MyPreservedDerivedClass1>());
            var r3 = (MyPreservedDerivedClass1)r2.M3;
            Assert.AreEqual("p2-m1", r3.M1);
            Assert.AreEqual("p2-m2", r3.M2);
            Assert.AreEqual("base", r3.M3.M1);
        }

        [TestCase((byte)1, (byte)1)]
        public void Slicing_PreservedClasses_WithCompactTypeId(byte encodingMajor, byte encodingMinor)
        {
            var encoding = new IceRpc.Encoding(encodingMajor, encodingMinor);
            byte[] buffer = new byte[1024 * 1024];
            var encoder = new IceEncoder(encoding, buffer, classFormat: FormatType.Sliced);

            var p2 = new MyPreservedDerivedClass2("p2-m1", "p2-m2", new MyBaseClass("base"));
            var p1 = new MyPreservedDerivedClass2("p1-m1", "p1-m2", p2);
            encoder.EncodeClass(p1, null);
            ReadOnlyMemory<byte> data = encoder.Finish().Span[0];

            // Create a factory that exclude 'MyPreservedDerivedClass2' compact type ID (56) and ensure that the class
            // is sliced and the Slices are preserved.
            var classFactory = new SlicingClassFactory(
                ClassFactory.Default,
                slicedClassCompactTypeIds: ImmutableList.Create(56));

            var decoder = new IceDecoder(data, encoding, classFactory: classFactory);
            Assert.Throws<InvalidDataException>(() => decoder.DecodeClass<MyPreservedDerivedClass2>(null));
            decoder = new IceDecoder(data, encoding, classFactory: classFactory);
            MyBaseClass r1 = decoder.DecodeClass<MyBaseClass>(null);

            // Marshal the sliced class
            buffer = new byte[1024 * 1024];
            encoder = new IceEncoder(encoding, buffer, classFormat: FormatType.Sliced);
            encoder.EncodeClass(r1, null);
            data = encoder.Finish().Span[0];

            // unmarshal again using the default factory, the unmarshaled class should contain the preserved Slices.
            decoder = new IceDecoder(data, encoding, classFactory: ClassFactory.Default);
            MyPreservedDerivedClass2 r2 = decoder.DecodeClass<MyPreservedDerivedClass2>(null);
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
