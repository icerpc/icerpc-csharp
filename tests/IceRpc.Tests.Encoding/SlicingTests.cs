// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace IceRpc.Tests.Encoding
{
    public class SlicingTests
    {
        [TestCase((byte)1, (byte)1)]
        [TestCase((byte)2, (byte)0)]
        public async Task Slicing_Classes(byte encodingMajor, byte encodingMinor)
        {
            Runtime.RegisterClassFactoriesFromAllAssemblies();
            await using var communicator = new Communicator();
            var encoding = new IceRpc.Encoding(encodingMajor, encodingMinor);
            byte[] buffer = new byte[1024 * 1024];
            var data = new List<ArraySegment<byte>>() { buffer };
            var ostr = new OutputStream(encoding,
                                        data,
                                        startAt: default,
                                        payloadEncoding: encoding,
                                        format: FormatType.Sliced);

            var p1 = new MyMostDerivedClass("most-derived", "derived", "base");
            ostr.WriteClass(p1, null);
            ostr.Finish();

            var istr = new InputStream(data[0], encoding, startEncapsulation: true);

            MyMostDerivedClass r = istr.ReadClass<MyMostDerivedClass>(null);

            Assert.AreEqual(p1.M1, r.M1);
            Assert.AreEqual(p1.M2, r.M2);
            Assert.AreEqual(p1.M3, r.M3);

            // Register a null class factory for MyMostDerivedClass to force Slicing
            Runtime.RegisterClassFactories(new IceRpc.ClassAttribute[]
                {
                    new ClassAttribute(MyMostDerivedClass.IceTypeId, -1, typeof(MyMostDerivedClass))
                });

            Assert.IsNull(Runtime.FindClassFactory(MyMostDerivedClass.IceTypeId));
            istr = new InputStream(data[0], encoding, startEncapsulation: true);
            Assert.Throws<InvalidDataException>(() => istr.ReadClass<MyMostDerivedClass>(null));

            istr = new InputStream(data[0], encoding, startEncapsulation: true);
            MyDerivedClass r1 = istr.ReadClass< MyDerivedClass>(null); // No formal type id optimization

            Assert.AreEqual(p1.M1, r1.M1);
            Assert.AreEqual(p1.M2, r1.M2);

            // Register a null class factory for MyDerivedClass to force Slicing
            Runtime.RegisterClassFactories(new IceRpc.ClassAttribute[]
                {
                    new ClassAttribute(MyDerivedClass.IceTypeId, -1, typeof(MyDerivedClass))
                });

            Assert.IsNull(Runtime.FindClassFactory(MyDerivedClass.IceTypeId));
            istr = new InputStream(data[0], encoding, startEncapsulation: true);
            Assert.Throws<InvalidDataException>(() => istr.ReadClass<MyDerivedClass>(null));

            istr = new InputStream(data[0], encoding, startEncapsulation: true);
            MyBaseClass r2 = istr.ReadClass<MyBaseClass>(null);

            Assert.AreEqual(p1.M1, r2.M1);

            // Register a null class factory for MyBaseClass to force Slicing
            Runtime.RegisterClassFactories(new IceRpc.ClassAttribute[]
                {
                    new ClassAttribute(MyBaseClass.IceTypeId, -1, typeof(MyBaseClass))
                });

            Assert.IsNull(Runtime.FindClassFactory(MyBaseClass.IceTypeId));
            istr = new InputStream(data[0], encoding, startEncapsulation: true);
            Assert.Throws<InvalidDataException>(() => istr.ReadClass<MyDerivedClass>(null));

            istr = new InputStream(data[0], encoding, startEncapsulation: true);
            Assert.DoesNotThrow(() => istr.ReadClass<AnyClass>(null));
        }

        [TestCase((byte)1, (byte)1)]
        [TestCase((byte)2, (byte)0)]
        public async Task Slicing_Exceptions(byte encodingMajor, byte encodingMinor)
        {
            Runtime.RegisterClassFactoriesFromAllAssemblies();
            await using var communicator = new Communicator();
            var encoding = new IceRpc.Encoding(encodingMajor, encodingMinor);
            byte[] buffer = new byte[1024 * 1024];
            var data = new List<ArraySegment<byte>>() { buffer };
            var ostr = new OutputStream(encoding,
                                        data,
                                        startAt: default,
                                        payloadEncoding: encoding,
                                        format: FormatType.Sliced);

            var p1 = new MyMostDerivedException("most-derived", "derived", "base");
            ostr.WriteException(p1);
            ostr.Finish();

            var istr = new InputStream(data[0], encoding, startEncapsulation: true);

            RemoteException r = istr.ReadException();

            Assert.IsInstanceOf<MyMostDerivedException>(r);

            MyMostDerivedException r1 = (MyMostDerivedException)r;

            Assert.AreEqual(p1.M1, r1.M1);
            Assert.AreEqual(p1.M2, r1.M2);
            Assert.AreEqual(p1.M3, r1.M3);

            // Register a null class factory for MyMostDerivedException to force Slicing
            Runtime.RegisterClassFactories(new IceRpc.ClassAttribute[]
                {
                    new ClassAttribute("::IceRpc::Tests::Encoding::MyMostDerivedException", -1, typeof(MyMostDerivedException))
                });

            Assert.IsNull(Runtime.FindClassFactory("::IceRpc::Tests::Encoding::MyMostDerivedException"));
            istr = new InputStream(data[0], encoding, startEncapsulation: true);

            r = istr.ReadException();
            Assert.IsInstanceOf<MyDerivedException>(r);
            Assert.IsNotInstanceOf<MyMostDerivedException>(r);
            MyDerivedException r2 = (MyDerivedException)r;
            Assert.AreEqual(p1.M1, r2.M1);
            Assert.AreEqual(p1.M2, r2.M2);

            // Register a null class factory for MyDerivedException to force Slicing
            Runtime.RegisterClassFactories(new IceRpc.ClassAttribute[]
                {
                    new ClassAttribute("::IceRpc::Tests::Encoding::MyDerivedException", -1, typeof(MyDerivedException))
                });

            Assert.IsNull(Runtime.FindClassFactory("::IceRpc::Tests::Encoding::MyDerivedException"));
            istr = new InputStream(data[0], encoding, startEncapsulation: true);

            r = istr.ReadException();
            Assert.IsNotInstanceOf<MyDerivedException>(r);
            Assert.IsInstanceOf<MyBaseException>(r);
            MyBaseException r3 = (MyBaseException)r;

            Assert.AreEqual(p1.M1, r2.M1);

            // Register a null class factory for MyBaseClass to force Slicing
            Runtime.RegisterClassFactories(new IceRpc.ClassAttribute[]
                {
                    new ClassAttribute("::IceRpc::Tests::Encoding::MyBaseException", -1, typeof(MyBaseException))
                });

            Assert.IsNull(Runtime.FindClassFactory("::IceRpc::Tests::Encoding::MyBaseException"));
            istr = new InputStream(data[0], encoding, startEncapsulation: true);
            r = istr.ReadException();
            Assert.IsNotInstanceOf<MyBaseException>(r);
        }
    }

    public sealed class ClassAttribute : IceRpc.ClassAttribute
    {
        internal override ClassFactory? ClassFactory => null;
        internal override RemoteExceptionFactory? ExceptionFactory => null;

        public ClassAttribute(string typeId, int compactTypeId, Type type)
            : base(typeId, compactTypeId, type)
        {
        }
    }
}
