// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace IceRpc.Tests.Encoding
{
    [Parallelizable(scope: ParallelScope.All)]
    public class SlicingTests
    {
        [TestCase((byte)1, (byte)1)]
        [TestCase((byte)2, (byte)0)]
        public void Slicing_Classes(byte encodingMajor, byte encodingMinor)
        {
            var encoding = new IceRpc.Encoding(encodingMajor, encodingMinor);
            byte[] buffer = new byte[1024 * 1024];
            var ostr = new OutputStream(encoding, buffer, classFormat: FormatType.Sliced);

            var p1 = new MyMostDerivedClass("most-derived", "derived", "base");
            ostr.WriteClass(p1, null);
            ReadOnlyMemory<byte> data = ostr.Finish().Span[0];

            // First we unmarshal the class using the default factories, no Slicing should occur in this case.
            var istr = new InputStream(data, encoding);
            MyMostDerivedClass r = istr.ReadClass<MyMostDerivedClass>(null);
            Assert.AreEqual(p1.M1, r.M1);
            Assert.AreEqual(p1.M2, r.M2);
            Assert.AreEqual(p1.M3, r.M3);

            // Remove the factory for 'MyMostDerivedClass' and ensure that the class is unmarshal
            // as 'MyDerivedClass' which is the base type and still know by input stream.
            var classFactories = new Dictionary<string, Lazy<ClassFactory>>(Runtime.TypeIdClassFactoryDictionary);
            classFactories.Remove(MyMostDerivedClass.IceTypeId);
            istr = new InputStream(data, encoding, typeIdClassFactories: classFactories);
            Assert.Throws<InvalidDataException>(() => istr.ReadClass<MyMostDerivedClass>(null));
            istr = new InputStream(data, encoding, typeIdClassFactories: classFactories);
            MyDerivedClass r1 = istr.ReadClass<MyDerivedClass>(null);
            Assert.That(r1.SlicedData, Is.Null);
            Assert.AreEqual(p1.M1, r1.M1);
            Assert.AreEqual(p1.M2, r1.M2);

            // Repeat removing the factory for 'MyDerivedClass'
            classFactories.Remove(MyDerivedClass.IceTypeId);
            istr = new InputStream(data, encoding, typeIdClassFactories: classFactories);
            Assert.Throws<InvalidDataException>(() => istr.ReadClass<MyDerivedClass>(null));
            istr = new InputStream(data, encoding, typeIdClassFactories: classFactories);
            MyBaseClass r2 = istr.ReadClass<MyBaseClass>(null);
            Assert.That(r2.SlicedData, Is.Null);
            Assert.AreEqual(p1.M1, r2.M1);

            // Repeat removing the factory for 'MyBaseClass'
            classFactories.Remove(MyBaseClass.IceTypeId);
            istr = new InputStream(data, encoding, typeIdClassFactories: classFactories);
            Assert.Throws<InvalidDataException>(() => istr.ReadClass<MyBaseClass>(null));
            istr = new InputStream(data, encoding, typeIdClassFactories: classFactories);
            Assert.DoesNotThrow(() => istr.ReadClass<AnyClass>(null));
        }

        [TestCase((byte)1, (byte)1)]
        public void Slicing_Classes_WithCompactTypeId(byte encodingMajor, byte encodingMinor)
        {
            var encoding = new IceRpc.Encoding(encodingMajor, encodingMinor);
            byte[] buffer = new byte[1024 * 1024];
            var ostr = new OutputStream(encoding, buffer, classFormat: FormatType.Sliced);

            var p1 = new MyCompactMostDerivedClass("most-derived", "derived", "base");
            ostr.WriteClass(p1, null);
            ReadOnlyMemory<byte> data = ostr.Finish().Span[0];

            // First we unmarshal the class using the default factories, no Slicing should occur in this case.
            var istr = new InputStream(data, encoding);
            MyCompactMostDerivedClass r = istr.ReadClass<MyCompactMostDerivedClass>(null);
            Assert.AreEqual(p1.M1, r.M1);
            Assert.AreEqual(p1.M2, r.M2);
            Assert.AreEqual(p1.M3, r.M3);

            // Remove the factory for 'MyCompactMostDerivedClass' and ensure that the class is unmarshal
            // as 'MyCompactDerivedClass' which is the base type and still know by input stream.
            var classFactories = new Dictionary<int, Lazy<ClassFactory>>(
                Runtime.CompactTypeIdClassFactoryDictionary);
            classFactories.Remove(3);
            istr = new InputStream(data,
                                   encoding,
                                   compactTypeIdClassFactories: classFactories);
            Assert.Throws<InvalidDataException>(() => istr.ReadClass<MyCompactMostDerivedClass>(null));
            istr = new InputStream(data,
                                   encoding,
                                   compactTypeIdClassFactories: classFactories);
            MyCompactDerivedClass r1 = istr.ReadClass<MyCompactDerivedClass>(null);
            Assert.That(r1.SlicedData, Is.Null);
            Assert.AreEqual(p1.M1, r1.M1);
            Assert.AreEqual(p1.M2, r1.M2);

            // Repeat removing the factory for 'MyCompactDerivedClass'
            classFactories.Remove(2);
            istr = new InputStream(data,
                                   encoding,
                                   compactTypeIdClassFactories: classFactories);
            Assert.Throws<InvalidDataException>(() => istr.ReadClass<MyCompactDerivedClass>(null));
            istr = new InputStream(data,
                                   encoding,
                                   compactTypeIdClassFactories: classFactories);
            MyCompactBaseClass r2 = istr.ReadClass<MyCompactBaseClass>(null);
            Assert.That(r2.SlicedData, Is.Null);
            Assert.AreEqual(p1.M1, r2.M1);

            // Repeat removing the factory for 'MyCompactBaseClass'
            classFactories.Remove(1);
            istr = new InputStream(data,
                                   encoding,
                                   compactTypeIdClassFactories: classFactories);
            Assert.Throws<InvalidDataException>(() => istr.ReadClass<MyCompactBaseClass>(null));
            istr = new InputStream(data,
                                   encoding,
                                   compactTypeIdClassFactories: classFactories);
            Assert.DoesNotThrow(() => istr.ReadClass<AnyClass>(null));
        }

        [TestCase((byte)1, (byte)1)]
        [TestCase((byte)2, (byte)0)]
        public void Slicing_Exceptions(byte encodingMajor, byte encodingMinor)
        {
            var encoding = new IceRpc.Encoding(encodingMajor, encodingMinor);
            byte[] buffer = new byte[1024 * 1024];
            var ostr = new OutputStream(encoding, buffer, classFormat: FormatType.Sliced);

            var p1 = new MyMostDerivedException("most-derived", "derived", "base");
            ostr.WriteException(p1);
            ReadOnlyMemory<byte> data = ostr.Finish().Span[0];

            // First we unmarshal the exception using the default factories, no Slicing should occur in this case.
            var istr = new InputStream(data, encoding);
            RemoteException r = istr.ReadException();
            Assert.That(r.SlicedData, Is.Null);
            Assert.That(r, Is.InstanceOf<MyMostDerivedException>());
            var r1 = (MyMostDerivedException)r;
            Assert.AreEqual(p1.M1, r1.M1);
            Assert.AreEqual(p1.M2, r1.M2);
            Assert.AreEqual(p1.M3, r1.M3);

            // Remove the factory for 'MyMostDerivedException' and ensure that the exception is unmarshal
            // as 'MyDerivedException' which is the base type and still know by input stream.
            var exceptionFactories = new Dictionary<string, Lazy<RemoteExceptionFactory>>(
                Runtime.TypeIdRemoteExceptionFactoryDictionary);
            exceptionFactories.Remove("::IceRpc::Tests::Encoding::MyMostDerivedException");
            istr = new InputStream(data,
                                   encoding,
                                   typeIdExceptionFactories: exceptionFactories);

            r = istr.ReadException();
            Assert.That(r.SlicedData, Is.Not.Null);
            Assert.That(r, Is.InstanceOf<MyDerivedException>());
            Assert.That(r, Is.Not.InstanceOf<MyMostDerivedException>());
            var r2 = (MyDerivedException)r;
            Assert.AreEqual(p1.M1, r2.M1);
            Assert.AreEqual(p1.M2, r2.M2);

            // Repeat removing the factory for 'MyDerivedException'
            exceptionFactories.Remove("::IceRpc::Tests::Encoding::MyDerivedException");
            istr = new InputStream(data,
                                   encoding,
                                   typeIdExceptionFactories: exceptionFactories);
            r = istr.ReadException();
            Assert.That(r.SlicedData, Is.Not.Null);
            Assert.That(r, Is.Not.InstanceOf<MyDerivedException>());
            Assert.That(r, Is.InstanceOf<MyBaseException>());
            var r3 = (MyBaseException)r;
            Assert.AreEqual(p1.M1, r3.M1);

            // Repeat removing the factory for 'MyBaseException'
            exceptionFactories.Remove("::IceRpc::Tests::Encoding::MyBaseException");
            istr = new InputStream(data,
                                   encoding,
                                   typeIdExceptionFactories: exceptionFactories);
            r = istr.ReadException();
            Assert.That(r.SlicedData, Is.Not.Null);
            Assert.That(r, Is.Not.InstanceOf<MyBaseException>());

            // Marshal the exception again to ensure all Slices are correctly preserved
            ostr = new OutputStream(encoding, buffer, classFormat: FormatType.Sliced);
            ostr.WriteException(r);
            data = ostr.Finish().Span[0];

            istr = new InputStream(data, encoding);
            r = istr.ReadException();
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
            var ostr = new OutputStream(encoding, buffer, classFormat: FormatType.Sliced);

            var p2 = new MyPreservedDerivedClass1("p2-m1", "p2-m2", new MyBaseClass("base"));
            var p1 = new MyPreservedDerivedClass1("p1-m1", "p1-m2", p2);
            ostr.WriteClass(p1, null);
            ReadOnlyMemory<byte> data = ostr.Finish().Span[0];

            // Unmarshal the 'MyPreservedDerivedClass1' class without its factory ensure the class is Sliced
            // and the Slices are preserved.
            var classFactories = new Dictionary<string, Lazy<ClassFactory>>(Runtime.TypeIdClassFactoryDictionary);
            bool factory = classFactories.Remove(MyPreservedDerivedClass1.IceTypeId);
            var istr = new InputStream(data,
                                       encoding,
                                       typeIdClassFactories: classFactories);
            Assert.Throws<InvalidDataException>(() => istr.ReadClass<MyPreservedDerivedClass1>(null));

            istr = new InputStream(data, encoding, typeIdClassFactories: classFactories);
            MyBaseClass r1 = istr.ReadClass<MyBaseClass>(null);
            Assert.That(r1.SlicedData, Is.Not.Null);

            // Marshal the sliced class
            buffer = new byte[1024 * 1024];
            ostr = new OutputStream(encoding, buffer, classFormat: FormatType.Sliced);
            ostr.WriteClass(r1, null);
            data = ostr.Finish().Span[0];

            // now add back the factory and read a unmarshal again, the unmarshaled class should contain the preserved
            // Slices.
            classFactories = new Dictionary<string, Lazy<ClassFactory>>(Runtime.TypeIdClassFactoryDictionary);

            istr = new InputStream(data, encoding, typeIdClassFactories: classFactories);
            MyPreservedDerivedClass1 r2 = istr.ReadClass<MyPreservedDerivedClass1>(null);
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
            var ostr = new OutputStream(encoding, buffer, classFormat: FormatType.Sliced);

            var p2 = new MyPreservedDerivedClass2("p2-m1", "p2-m2", new MyBaseClass("base"));
            var p1 = new MyPreservedDerivedClass2("p1-m1", "p1-m2", p2);
            ostr.WriteClass(p1, null);
            ReadOnlyMemory<byte> data = ostr.Finish().Span[0];

            // Unmarshal the 'MyPreservedDerivedClass2' class without its factory to ensure that the class is Sliced
            // and the Slices are preserved.
            var classFactories = new Dictionary<int, Lazy<ClassFactory>>(Runtime.CompactTypeIdClassFactoryDictionary);
            bool factory = classFactories.Remove(56);
            var istr = new InputStream(data,
                                       encoding,
                                       compactTypeIdClassFactories: classFactories);
            Assert.Throws<InvalidDataException>(() => istr.ReadClass<MyPreservedDerivedClass2>(null));
            istr = new InputStream(data,
                                       encoding,
                                       compactTypeIdClassFactories: classFactories);
            MyBaseClass r1 = istr.ReadClass<MyBaseClass>(null);

            // Marshal the sliced class
            buffer = new byte[1024 * 1024];
            ostr = new OutputStream(encoding, buffer, classFormat: FormatType.Sliced);
            ostr.WriteClass(r1, null);
            data = ostr.Finish().Span[0];

            // now add back the factory and unmarshal it again, the unmarshaled class should contain the preserved
            // Slices.
            classFactories = new Dictionary<int, Lazy<ClassFactory>>(Runtime.CompactTypeIdClassFactoryDictionary);
            istr = new InputStream(data,
                                   encoding,
                                   compactTypeIdClassFactories: classFactories);
            MyPreservedDerivedClass2 r2 = istr.ReadClass<MyPreservedDerivedClass2>(null);
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
