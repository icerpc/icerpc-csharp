// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System.Reflection;

namespace IceRpc.Tests.Api
{
    [Timeout(30000)]
    public class ClassFactory
    {
        [Test]
        public void ClassFactory_CreateInstance()
        {
            IceRpc.IObjectFactory<Ice11Decoder> factory = IceRpc.ClassFactory.Default;

            Ice11Decoder decoder = null!;

            // The default factory knows about types defined in IceRpc and entry assemblies
            Assert.That(factory.CreateInstance("::IceRpc::ServiceNotFoundException", decoder), Is.Not.Null);

            // The default factory doesn't know about types defined in separated assemblies
            Assert.That(factory.CreateInstance(MyClassA.IceTypeId, decoder), Is.Null);
            Assert.That(factory.CreateInstance(MyClassB.IceTypeId, decoder), Is.Null);
            Assert.That(factory.CreateInstance(MyClassC.IceTypeId, decoder), Is.Null);
            Assert.That(factory.CreateInstance(MyClassD.IceTypeId, decoder), Is.Null);

            Assert.That(factory.CreateInstance("1", decoder), Is.Null);
            Assert.That(factory.CreateInstance("2", decoder), Is.Null);
            Assert.That(factory.CreateInstance("3", decoder), Is.Null);
            Assert.That(factory.CreateInstance("4", decoder), Is.Null);

            Assert.That(factory.CreateInstance("::IceRpc::Tests::Api::MyExceptionA", decoder), Is.Null);
            Assert.That(factory.CreateInstance("::IceRpc::Tests::Api::MyExceptionB", decoder), Is.Null);
            Assert.That(factory.CreateInstance("::IceRpc::Tests::Api::MyExceptionC", decoder), Is.Null);
            Assert.That(factory.CreateInstance("::IceRpc::Tests::Api::MyExceptionD", decoder), Is.Null);

            factory = new IceRpc.ClassFactory(new Assembly[] { typeof(MyClassA).Assembly });
            Assert.That(factory.CreateInstance(MyClassA.IceTypeId, decoder), Is.Not.Null);

            Assert.That(factory.CreateInstance(MyClassB.IceTypeId, decoder), Is.Null);
            Assert.That(factory.CreateInstance(MyClassC.IceTypeId, decoder), Is.Null);
            Assert.That(factory.CreateInstance(MyClassD.IceTypeId, decoder), Is.Null);

            Assert.That(factory.CreateInstance("1", decoder), Is.Not.Null);

            Assert.That(factory.CreateInstance("2", decoder), Is.Null);
            Assert.That(factory.CreateInstance("3", decoder), Is.Null);
            Assert.That(factory.CreateInstance("4", decoder), Is.Null);

            Assert.That(factory.CreateInstance("::IceRpc::Tests::Api::MyExceptionA", decoder), Is.Not.Null);

            Assert.That(factory.CreateInstance("::IceRpc::Tests::Api::MyExceptionB", decoder), Is.Null);
            Assert.That(factory.CreateInstance("::IceRpc::Tests::Api::MyExceptionC", decoder), Is.Null);
            Assert.That(factory.CreateInstance("::IceRpc::Tests::Api::MyExceptionD", decoder), Is.Null);

            // Create a class factory that knows about A and B assemblies
            factory = new IceRpc.ClassFactory(new Assembly[]
            {
                typeof(MyClassA).Assembly,
                typeof(MyClassB).Assembly
            });
            Assert.That(factory.CreateInstance(MyClassA.IceTypeId, decoder), Is.Not.Null);
            Assert.That(factory.CreateInstance(MyClassB.IceTypeId, decoder), Is.Not.Null);

            Assert.That(factory.CreateInstance(MyClassC.IceTypeId, decoder), Is.Null);
            Assert.That(factory.CreateInstance(MyClassD.IceTypeId, decoder), Is.Null);

            Assert.That(factory.CreateInstance("1", decoder), Is.Not.Null);
            Assert.That(factory.CreateInstance("2", decoder), Is.Not.Null);

            Assert.That(factory.CreateInstance("3", decoder), Is.Null);
            Assert.That(factory.CreateInstance("4", decoder), Is.Null);

            Assert.That(factory.CreateInstance("::IceRpc::Tests::Api::MyExceptionA", decoder), Is.Not.Null);
            Assert.That(factory.CreateInstance("::IceRpc::Tests::Api::MyExceptionB", decoder), Is.Not.Null);

            Assert.That(factory.CreateInstance("::IceRpc::Tests::Api::MyExceptionC", decoder), Is.Null);
            Assert.That(factory.CreateInstance("::IceRpc::Tests::Api::MyExceptionD", decoder), Is.Null);

            // Create a class factory that knows about A, B and C assemblies
            factory = new IceRpc.ClassFactory(new Assembly[]
            {
                typeof(MyClassA).Assembly,
                typeof(MyClassB).Assembly,
                typeof(MyClassC).Assembly
            });
            Assert.That(factory.CreateInstance(MyClassA.IceTypeId, decoder), Is.Not.Null);
            Assert.That(factory.CreateInstance(MyClassB.IceTypeId, decoder), Is.Not.Null);
            Assert.That(factory.CreateInstance(MyClassC.IceTypeId, decoder), Is.Not.Null);

            Assert.That(factory.CreateInstance(MyClassD.IceTypeId, decoder), Is.Null);

            Assert.That(factory.CreateInstance("1", decoder), Is.Not.Null);
            Assert.That(factory.CreateInstance("2", decoder), Is.Not.Null);
            Assert.That(factory.CreateInstance("3", decoder), Is.Not.Null);

            Assert.That(factory.CreateInstance("4", decoder), Is.Null);

            Assert.That(factory.CreateInstance("::IceRpc::Tests::Api::MyExceptionA", decoder), Is.Not.Null);
            Assert.That(factory.CreateInstance("::IceRpc::Tests::Api::MyExceptionB", decoder), Is.Not.Null);
            Assert.That(factory.CreateInstance("::IceRpc::Tests::Api::MyExceptionC", decoder), Is.Not.Null);

            Assert.That(factory.CreateInstance("::IceRpc::Tests::Api::MyExceptionD", decoder), Is.Null);

            // Create a class factory that knows about A, B, C and D assemblies
            factory = new IceRpc.ClassFactory(new Assembly[]
            {
                typeof(MyClassA).Assembly,
                typeof(MyClassB).Assembly,
                typeof(MyClassC).Assembly,
                typeof(MyClassD).Assembly
            });
            Assert.That(factory.CreateInstance(MyClassA.IceTypeId, decoder), Is.Not.Null);
            Assert.That(factory.CreateInstance(MyClassB.IceTypeId, decoder), Is.Not.Null);
            Assert.That(factory.CreateInstance(MyClassC.IceTypeId, decoder), Is.Not.Null);
            Assert.That(factory.CreateInstance(MyClassD.IceTypeId, decoder), Is.Not.Null);

            Assert.That(factory.CreateInstance("1", decoder), Is.Not.Null);
            Assert.That(factory.CreateInstance("2", decoder), Is.Not.Null);
            Assert.That(factory.CreateInstance("3", decoder), Is.Not.Null);
            Assert.That(factory.CreateInstance("4", decoder), Is.Not.Null);

            Assert.That(factory.CreateInstance("::IceRpc::Tests::Api::MyExceptionA", decoder), Is.Not.Null);
            Assert.That(factory.CreateInstance("::IceRpc::Tests::Api::MyExceptionB", decoder), Is.Not.Null);
            Assert.That(factory.CreateInstance("::IceRpc::Tests::Api::MyExceptionC", decoder), Is.Not.Null);
            Assert.That(factory.CreateInstance("::IceRpc::Tests::Api::MyExceptionD", decoder), Is.Not.Null);
        }
    }
}
