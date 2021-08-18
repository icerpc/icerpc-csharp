// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System.Reflection;

namespace IceRpc.Tests.Api
{
    [Timeout(30000)]
    public class ClassFactory
    {
        [Test]
        public void ClassFactory_CreateClass()
        {
            IceRpc.IClassFactory factory = IceRpc.ClassFactory.Default;

            // The default factory knows about types defined in IceRpc and entry assemblies
            Assert.That(factory.CreateClass("::IceRpc::ServiceNotFoundException"), Is.Not.Null);

            // The default factory doesn't know about types defined in separated assemblies
            Assert.That(factory.CreateClass(MyClassA.IceTypeId), Is.Null);
            Assert.That(factory.CreateClass(MyClassB.IceTypeId), Is.Null);
            Assert.That(factory.CreateClass(MyClassC.IceTypeId), Is.Null);
            Assert.That(factory.CreateClass(MyClassD.IceTypeId), Is.Null);

            Assert.That(factory.CreateClass("1"), Is.Null);
            Assert.That(factory.CreateClass("2"), Is.Null);
            Assert.That(factory.CreateClass("3"), Is.Null);
            Assert.That(factory.CreateClass("4"), Is.Null);

            Assert.That(factory.CreateClass("::IceRpc::Tests::Api::MyExceptionA"), Is.Null);
            Assert.That(factory.CreateClass("::IceRpc::Tests::Api::MyExceptionB"), Is.Null);
            Assert.That(factory.CreateClass("::IceRpc::Tests::Api::MyExceptionC"), Is.Null);
            Assert.That(factory.CreateClass("::IceRpc::Tests::Api::MyExceptionD"), Is.Null);

            factory = new IceRpc.ClassFactory(new Assembly[] { typeof(MyClassA).Assembly });
            Assert.That(factory.CreateClass(MyClassA.IceTypeId), Is.Not.Null);

            Assert.That(factory.CreateClass(MyClassB.IceTypeId), Is.Null);
            Assert.That(factory.CreateClass(MyClassC.IceTypeId), Is.Null);
            Assert.That(factory.CreateClass(MyClassD.IceTypeId), Is.Null);

            Assert.That(factory.CreateClass("1"), Is.Not.Null);

            Assert.That(factory.CreateClass("2"), Is.Null);
            Assert.That(factory.CreateClass("3"), Is.Null);
            Assert.That(factory.CreateClass("4"), Is.Null);

            Assert.That(factory.CreateClass("::IceRpc::Tests::Api::MyExceptionA"), Is.Not.Null);

            Assert.That(factory.CreateClass("::IceRpc::Tests::Api::MyExceptionB"), Is.Null);
            Assert.That(factory.CreateClass("::IceRpc::Tests::Api::MyExceptionC"), Is.Null);
            Assert.That(factory.CreateClass("::IceRpc::Tests::Api::MyExceptionD"), Is.Null);

            // Create a class factory that knows about A and B assemblies
            factory = new IceRpc.ClassFactory(new Assembly[]
            {
                typeof(MyClassA).Assembly,
                typeof(MyClassB).Assembly
            });
            Assert.That(factory.CreateClass(MyClassA.IceTypeId), Is.Not.Null);
            Assert.That(factory.CreateClass(MyClassB.IceTypeId), Is.Not.Null);

            Assert.That(factory.CreateClass(MyClassC.IceTypeId), Is.Null);
            Assert.That(factory.CreateClass(MyClassD.IceTypeId), Is.Null);

            Assert.That(factory.CreateClass("1"), Is.Not.Null);
            Assert.That(factory.CreateClass("2"), Is.Not.Null);

            Assert.That(factory.CreateClass("3"), Is.Null);
            Assert.That(factory.CreateClass("4"), Is.Null);

            Assert.That(factory.CreateClass("::IceRpc::Tests::Api::MyExceptionA"), Is.Not.Null);
            Assert.That(factory.CreateClass("::IceRpc::Tests::Api::MyExceptionB"), Is.Not.Null);

            Assert.That(factory.CreateClass("::IceRpc::Tests::Api::MyExceptionC"), Is.Null);
            Assert.That(factory.CreateClass("::IceRpc::Tests::Api::MyExceptionD"), Is.Null);

            // Create a class factory that knows about A, B and C assemblies
            factory = new IceRpc.ClassFactory(new Assembly[]
            {
                typeof(MyClassA).Assembly,
                typeof(MyClassB).Assembly,
                typeof(MyClassC).Assembly
            });
            Assert.That(factory.CreateClass(MyClassA.IceTypeId), Is.Not.Null);
            Assert.That(factory.CreateClass(MyClassB.IceTypeId), Is.Not.Null);
            Assert.That(factory.CreateClass(MyClassC.IceTypeId), Is.Not.Null);

            Assert.That(factory.CreateClass(MyClassD.IceTypeId), Is.Null);

            Assert.That(factory.CreateClass("1"), Is.Not.Null);
            Assert.That(factory.CreateClass("2"), Is.Not.Null);
            Assert.That(factory.CreateClass("3"), Is.Not.Null);

            Assert.That(factory.CreateClass("4"), Is.Null);

            Assert.That(factory.CreateClass("::IceRpc::Tests::Api::MyExceptionA"), Is.Not.Null);
            Assert.That(factory.CreateClass("::IceRpc::Tests::Api::MyExceptionB"), Is.Not.Null);
            Assert.That(factory.CreateClass("::IceRpc::Tests::Api::MyExceptionC"), Is.Not.Null);

            Assert.That(factory.CreateClass("::IceRpc::Tests::Api::MyExceptionD"), Is.Null);

            // Create a class factory that knows about A, B, C and D assemblies
            factory = new IceRpc.ClassFactory(new Assembly[]
            {
                typeof(MyClassA).Assembly,
                typeof(MyClassB).Assembly,
                typeof(MyClassC).Assembly,
                typeof(MyClassD).Assembly
            });
            Assert.That(factory.CreateClass(MyClassA.IceTypeId), Is.Not.Null);
            Assert.That(factory.CreateClass(MyClassB.IceTypeId), Is.Not.Null);
            Assert.That(factory.CreateClass(MyClassC.IceTypeId), Is.Not.Null);
            Assert.That(factory.CreateClass(MyClassD.IceTypeId), Is.Not.Null);

            Assert.That(factory.CreateClass("1"), Is.Not.Null);
            Assert.That(factory.CreateClass("2"), Is.Not.Null);
            Assert.That(factory.CreateClass("3"), Is.Not.Null);
            Assert.That(factory.CreateClass("4"), Is.Not.Null);

            Assert.That(factory.CreateClass("::IceRpc::Tests::Api::MyExceptionA"), Is.Not.Null);
            Assert.That(factory.CreateClass("::IceRpc::Tests::Api::MyExceptionB"), Is.Not.Null);
            Assert.That(factory.CreateClass("::IceRpc::Tests::Api::MyExceptionC"), Is.Not.Null);
            Assert.That(factory.CreateClass("::IceRpc::Tests::Api::MyExceptionD"), Is.Not.Null);
        }
    }
}
