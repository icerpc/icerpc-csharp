// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System.Collections.Immutable;
using System.Reflection;

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

        [TestCase("1.1")]
        [TestCase("2.0")]
        public void Slicing_Classes(string encodingStr)
        {
            var encoding = IceRpc.Encoding.FromString(encodingStr);
            byte[] buffer = new byte[1024 * 1024];
            var encoder = new IceEncoder(encoding, buffer, classFormat: FormatType.Sliced);

            var p1 = new MyMostDerivedClass("most-derived", "derived", "base");
            encoder.EncodeClass(p1);
            ReadOnlyMemory<byte> data = encoder.Finish().Span[0];

            // Create a factory that knows about all the types using in this test
            var classFactory = new ClassFactory(new Assembly[]
            {
                typeof(MyMostDerivedClass).Assembly,
                typeof(MyDerivedClass).Assembly,
                typeof(MyBaseClass).Assembly
            });

            // First we unmarshal the class using the factory that know all the types, no Slicing should occur in this case.
            var decoder = new IceDecoder(data, encoding, classFactory: classFactory);
            MyMostDerivedClass r = decoder.DecodeClass<MyMostDerivedClass>();
            Assert.AreEqual(p1.M1, r.M1);
            Assert.AreEqual(p1.M2, r.M2);
            Assert.AreEqual(p1.M3, r.M3);

            // Create a factory that exclude 'MyMostDerivedClass' type ID and ensure that the class is unmarshaled as
            // 'MyDerivedClass' which is the base type.
            var slicingClassFactory = new SlicingClassFactory(
                classFactory,
                slicedClassTypeIds: ImmutableList.Create(MyMostDerivedClass.IceTypeId));

            decoder = new IceDecoder(data, encoding, classFactory: slicingClassFactory);
            Assert.Throws<InvalidDataException>(() => decoder.DecodeClass<MyMostDerivedClass>());
            decoder = new IceDecoder(data, encoding, classFactory: slicingClassFactory);
            MyDerivedClass r1 = decoder.DecodeClass<MyDerivedClass>();
            Assert.That(r1.SlicedData, Is.Null);
            Assert.AreEqual(p1.M1, r1.M1);
            Assert.AreEqual(p1.M2, r1.M2);

            // Repeat with a factory that also excludes 'MyDerivedClass' type ID
            slicingClassFactory = new SlicingClassFactory(
                classFactory,
                slicedClassTypeIds: ImmutableList.Create(MyMostDerivedClass.IceTypeId, MyDerivedClass.IceTypeId));

            decoder = new IceDecoder(data, encoding, classFactory: slicingClassFactory);
            Assert.Throws<InvalidDataException>(() => decoder.DecodeClass<MyDerivedClass>());
            decoder = new IceDecoder(data, encoding, classFactory: slicingClassFactory);
            MyBaseClass r2 = decoder.DecodeClass<MyBaseClass>();
            Assert.That(r2.SlicedData, Is.Null);
            Assert.AreEqual(p1.M1, r2.M1);

            // Repeat with a factory that also excludes 'MyBaseClass' type ID
            slicingClassFactory = new SlicingClassFactory(
                    classFactory,
                    slicedClassTypeIds: ImmutableList.Create(
                        MyMostDerivedClass.IceTypeId,
                        MyDerivedClass.IceTypeId,
                        MyBaseClass.IceTypeId));

            decoder = new IceDecoder(data, encoding, classFactory: slicingClassFactory);
            Assert.Throws<InvalidDataException>(() => decoder.DecodeClass<MyBaseClass>());
            decoder = new IceDecoder(data, encoding, classFactory: slicingClassFactory);
            Assert.DoesNotThrow(() => decoder.DecodeClass<AnyClass>());
        }

        [TestCase("1.1")]
        public void Slicing_Classes_WithCompactTypeId(string encodingStr)
        {
            var encoding = IceRpc.Encoding.FromString(encodingStr);
            byte[] buffer = new byte[1024 * 1024];
            var encoder = new IceEncoder(encoding, buffer, classFormat: FormatType.Sliced);

            var p1 = new MyCompactMostDerivedClass("most-derived", "derived", "base");
            encoder.EncodeClass(p1);
            ReadOnlyMemory<byte> data = encoder.Finish().Span[0];

            // Create a factory that knows about all the types using in this test
            var classFactory = new ClassFactory(new Assembly[]
            {
                typeof(MyCompactMostDerivedClass).Assembly,
                typeof(MyCompactDerivedClass).Assembly,
                typeof(MyCompactBaseClass).Assembly
            });

            // First we unmarshal the class using the default factories, no Slicing should occur in this case.
            var decoder = new IceDecoder(data, encoding, classFactory: classFactory);
            MyCompactMostDerivedClass r = decoder.DecodeClass<MyCompactMostDerivedClass>();
            Assert.AreEqual(p1.M1, r.M1);
            Assert.AreEqual(p1.M2, r.M2);
            Assert.AreEqual(p1.M3, r.M3);

            // Create a factory that exclude 'MyCompactMostDerivedClass' compact type ID (3) and ensure that
            // the class is unmarshaled as 'MyCompactDerivedClass' which is the base type.
            var slicingClassFactory = new SlicingClassFactory(
                classFactory,
                slicedClassCompactTypeIds: ImmutableList.Create(3));
            decoder = new IceDecoder(data, encoding, classFactory: slicingClassFactory);
            Assert.Throws<InvalidDataException>(() => decoder.DecodeClass<MyCompactMostDerivedClass>());
            decoder = new IceDecoder(data, encoding, classFactory: slicingClassFactory);
            MyCompactDerivedClass r1 = decoder.DecodeClass<MyCompactDerivedClass>();
            Assert.That(r1.SlicedData, Is.Null);
            Assert.AreEqual(p1.M1, r1.M1);
            Assert.AreEqual(p1.M2, r1.M2);

            // Repeat with a factory that also excludes 'MyCompactDerivedClass' compact type ID (2)
            slicingClassFactory = slicingClassFactory = new SlicingClassFactory(
                classFactory,
                slicedClassCompactTypeIds: ImmutableList.Create(3, 2));
            decoder = new IceDecoder(data, encoding, classFactory: slicingClassFactory);
            Assert.Throws<InvalidDataException>(() => decoder.DecodeClass<MyCompactDerivedClass>());
            decoder = new IceDecoder(data, encoding, classFactory: slicingClassFactory);
            MyCompactBaseClass r2 = decoder.DecodeClass<MyCompactBaseClass>();
            Assert.That(r2.SlicedData, Is.Null);
            Assert.AreEqual(p1.M1, r2.M1);

            // Repeat with a factory that also excludes 'MyCompactBaseClass' compact type ID (1)
            slicingClassFactory = slicingClassFactory = new SlicingClassFactory(
                classFactory,
                slicedClassCompactTypeIds: ImmutableList.Create(3, 2, 1));
            decoder = new IceDecoder(data, encoding, classFactory: slicingClassFactory);
            Assert.Throws<InvalidDataException>(() => decoder.DecodeClass<MyCompactBaseClass>());
            decoder = new IceDecoder(data, encoding, classFactory: slicingClassFactory);
            Assert.DoesNotThrow(() => decoder.DecodeClass<AnyClass>());
        }

        [TestCase("1.1")]
        [TestCase("2.0")]
        public void Slicing_Exceptions(string encodingStr)
        {
            var encoding = IceRpc.Encoding.FromString(encodingStr);
            byte[] buffer = new byte[1024 * 1024];
            var encoder = new IceEncoder(encoding, buffer, classFormat: FormatType.Sliced);

            var p1 = new MyMostDerivedException("most-derived", "derived", "base");
            encoder.EncodeException(p1);
            ReadOnlyMemory<byte> data = encoder.Finish().Span[0];

            // Create a factory that knows about all the types using in this test
            var classFactory = new ClassFactory(new Assembly[]
            {
                typeof(MyMostDerivedException).Assembly,
                typeof(MyMostDerivedException).Assembly,
                typeof(MyBaseException).Assembly
            });

            // First we unmarshal the exception using the factory that knows all the types, no Slicing should occur in this case.
            var decoder = new IceDecoder(data, encoding, classFactory: classFactory);
            RemoteException r = decoder.DecodeException();
            Assert.That(r.SlicedData, Is.Null);
            Assert.That(r, Is.InstanceOf<MyMostDerivedException>());
            var r1 = (MyMostDerivedException)r;
            Assert.AreEqual(p1.M1, r1.M1);
            Assert.AreEqual(p1.M2, r1.M2);
            Assert.AreEqual(p1.M3, r1.M3);

            // Create a factory that exclude 'MyMostDerivedException' type ID and ensure that the class is unmarshaled
            // as 'MyDerivedException' which is the base type.
            var slicingClassFactory = new SlicingClassFactory(
                classFactory,
                slicedExceptionTypeIds: ImmutableList.Create("::IceRpc::Tests::Encoding::MyMostDerivedException"));

            decoder = new IceDecoder(data, encoding, classFactory: slicingClassFactory);

            r = decoder.DecodeException();
            Assert.That(r.SlicedData, Is.Not.Null);
            Assert.That(r, Is.InstanceOf<MyDerivedException>());
            Assert.That(r, Is.Not.InstanceOf<MyMostDerivedException>());
            var r2 = (MyDerivedException)r;
            Assert.AreEqual(p1.M1, r2.M1);
            Assert.AreEqual(p1.M2, r2.M2);

            // Repeat with a factory that also excludes 'MyDerivedException' type ID
            slicingClassFactory = new SlicingClassFactory(
                classFactory,
                slicedExceptionTypeIds: ImmutableList.Create(
                    "::IceRpc::Tests::Encoding::MyMostDerivedException",
                    "::IceRpc::Tests::Encoding::MyDerivedException"));

            decoder = new IceDecoder(data, encoding, classFactory: slicingClassFactory);
            r = decoder.DecodeException();
            Assert.That(r.SlicedData, Is.Not.Null);
            Assert.That(r, Is.Not.InstanceOf<MyDerivedException>());
            Assert.That(r, Is.InstanceOf<MyBaseException>());
            var r3 = (MyBaseException)r;
            Assert.AreEqual(p1.M1, r3.M1);

            // Repeat with a factory that also excludes 'MyBaseException' type ID
            slicingClassFactory = new SlicingClassFactory(
                classFactory,
                slicedExceptionTypeIds: ImmutableList.Create(
                    "::IceRpc::Tests::Encoding::MyMostDerivedException",
                    "::IceRpc::Tests::Encoding::MyDerivedException",
                    "::IceRpc::Tests::Encoding::MyBaseException"));

            decoder = new IceDecoder(data, encoding, classFactory: slicingClassFactory);
            r = decoder.DecodeException();
            Assert.That(r.SlicedData, Is.Not.Null);
            Assert.That(r, Is.Not.InstanceOf<MyBaseException>());

            // Marshal the exception again to ensure all Slices are correctly preserved
            encoder = new IceEncoder(encoding, buffer, classFormat: FormatType.Sliced);
            encoder.EncodeException(r);
            data = encoder.Finish().Span[0];

            decoder = new IceDecoder(data, encoding, classFactory: classFactory);
            r = decoder.DecodeException();
            Assert.That(r.SlicedData, Is.Null);
            Assert.That(r, Is.InstanceOf<MyMostDerivedException>());
            r1 = (MyMostDerivedException)r;
            Assert.AreEqual(p1.M1, r1.M1);
            Assert.AreEqual(p1.M2, r1.M2);
            Assert.AreEqual(p1.M3, r1.M3);
        }

        [TestCase("1.1")]
        [TestCase("2.0")]
        public void Slicing_PreservedClasses(string encodingStr)
        {
            var encoding = IceRpc.Encoding.FromString(encodingStr);
            byte[] buffer = new byte[1024 * 1024];
            var encoder = new IceEncoder(encoding, buffer, classFormat: FormatType.Sliced);

            var p2 = new MyPreservedDerivedClass1("p2-m1", "p2-m2", new MyBaseClass("base"));
            var p1 = new MyPreservedDerivedClass1("p1-m1", "p1-m2", p2);
            encoder.EncodeClass(p1);
            ReadOnlyMemory<byte> data = encoder.Finish().Span[0];

            // Create a factory that knows about all the types using in this test
            var classFactory = new ClassFactory(new Assembly[] { typeof(MyPreservedDerivedClass1).Assembly });

            // Create a factory that exclude 'MyPreservedDerivedClass1' type ID and ensure that the class is sliced and
            // the Slices are preserved.
            var slicingClassFactory = new SlicingClassFactory(
                classFactory,
                slicedClassTypeIds: ImmutableList.Create(MyPreservedDerivedClass1.IceTypeId));

            var decoder = new IceDecoder(data, encoding, classFactory: slicingClassFactory);
            Assert.Throws<InvalidDataException>(() => decoder.DecodeClass<MyPreservedDerivedClass1>());

            decoder = new IceDecoder(data, encoding, classFactory: slicingClassFactory);
            MyBaseClass r1 = decoder.DecodeClass<MyBaseClass>();
            Assert.That(r1.SlicedData, Is.Not.Null);

            // Marshal the sliced class
            buffer = new byte[1024 * 1024];
            encoder = new IceEncoder(encoding, buffer, classFormat: FormatType.Sliced);
            encoder.EncodeClass(r1);
            data = encoder.Finish().Span[0];

            // unmarshal again using the default factory, the unmarshaled class should contain the preserved Slices.
            decoder = new IceDecoder(data, encoding, classFactory: classFactory);
            MyPreservedDerivedClass1 r2 = decoder.DecodeClass<MyPreservedDerivedClass1>();
            Assert.That(r2.SlicedData, Is.Null);
            Assert.AreEqual("p1-m1", r2.M1);
            Assert.AreEqual("p1-m2", r2.M2);
            Assert.That(r2.M3, Is.InstanceOf<MyPreservedDerivedClass1>());
            var r3 = (MyPreservedDerivedClass1)r2.M3;
            Assert.AreEqual("p2-m1", r3.M1);
            Assert.AreEqual("p2-m2", r3.M2);
            Assert.AreEqual("base", r3.M3.M1);
        }

        [TestCase("1.1")]
        public void Slicing_PreservedClasses_WithCompactTypeId(string encodingStr)
        {
            var encoding = IceRpc.Encoding.FromString(encodingStr);
            byte[] buffer = new byte[1024 * 1024];
            var encoder = new IceEncoder(encoding, buffer, classFormat: FormatType.Sliced);

            var p2 = new MyPreservedDerivedClass2("p2-m1", "p2-m2", new MyBaseClass("base"));
            var p1 = new MyPreservedDerivedClass2("p1-m1", "p1-m2", p2);
            encoder.EncodeClass(p1);
            ReadOnlyMemory<byte> data = encoder.Finish().Span[0];

            // Create a factory that knows about all the types using in this test
            var classFactory = new ClassFactory(new Assembly[] { typeof(MyPreservedDerivedClass2).Assembly });

            // Create a factory that exclude 'MyPreservedDerivedClass2' compact type ID (56) and ensure that the class
            // is sliced and the Slices are preserved.
            var slicingClassFactory = new SlicingClassFactory(
                classFactory,
                slicedClassCompactTypeIds: ImmutableList.Create(56));

            var decoder = new IceDecoder(data, encoding, classFactory: slicingClassFactory);
            Assert.Throws<InvalidDataException>(() => decoder.DecodeClass<MyPreservedDerivedClass2>());
            decoder = new IceDecoder(data, encoding, classFactory: slicingClassFactory);
            MyBaseClass r1 = decoder.DecodeClass<MyBaseClass>();

            // Marshal the sliced class
            buffer = new byte[1024 * 1024];
            encoder = new IceEncoder(encoding, buffer, classFormat: FormatType.Sliced);
            encoder.EncodeClass(r1);
            data = encoder.Finish().Span[0];

            // unmarshal again using the default factory, the unmarshaled class should contain the preserved Slices.
            decoder = new IceDecoder(data, encoding, classFactory: classFactory);
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
