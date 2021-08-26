// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using NUnit.Framework;
using System.Reflection;

namespace IceRpc.Tests.Api
{
    [Timeout(30000)]
    public class Activator
    {
        [Test]
        public void Activator_CreateInstance()
        {
            IActivator<Ice11Decoder> activator = Ice11Decoder.GetActivator(typeof(Ice11Decoder).Assembly);

            Ice11Decoder decoder = null!;

            Assert.That(activator.CreateInstance("::IceRpc::ServiceNotFoundException", decoder), Is.Not.Null);

            // The default activator doesn't know about types defined in separated assemblies
            Assert.That(activator.CreateInstance(MyClassA.IceTypeId, decoder), Is.Null);
            Assert.That(activator.CreateInstance(MyClassB.IceTypeId, decoder), Is.Null);
            Assert.That(activator.CreateInstance(MyClassC.IceTypeId, decoder), Is.Null);
            Assert.That(activator.CreateInstance(MyClassD.IceTypeId, decoder), Is.Null);

            Assert.That(activator.CreateInstance("1", decoder), Is.Null);
            Assert.That(activator.CreateInstance("2", decoder), Is.Null);
            Assert.That(activator.CreateInstance("3", decoder), Is.Null);
            Assert.That(activator.CreateInstance("4", decoder), Is.Null);

            Assert.That(activator.CreateInstance("::IceRpc::Tests::Api::MyExceptionA", decoder), Is.Null);
            Assert.That(activator.CreateInstance("::IceRpc::Tests::Api::MyExceptionB", decoder), Is.Null);
            Assert.That(activator.CreateInstance("::IceRpc::Tests::Api::MyExceptionC", decoder), Is.Null);
            Assert.That(activator.CreateInstance("::IceRpc::Tests::Api::MyExceptionD", decoder), Is.Null);

            activator = Ice11Decoder.GetActivator(typeof(MyClassA).Assembly);
            Assert.That(activator.CreateInstance(MyClassA.IceTypeId, decoder), Is.Not.Null);

            Assert.That(activator.CreateInstance(MyClassB.IceTypeId, decoder), Is.Null);
            Assert.That(activator.CreateInstance(MyClassC.IceTypeId, decoder), Is.Null);
            Assert.That(activator.CreateInstance(MyClassD.IceTypeId, decoder), Is.Null);

            Assert.That(activator.CreateInstance("1", decoder), Is.Not.Null);

            Assert.That(activator.CreateInstance("2", decoder), Is.Null);
            Assert.That(activator.CreateInstance("3", decoder), Is.Null);
            Assert.That(activator.CreateInstance("4", decoder), Is.Null);

            Assert.That(activator.CreateInstance("::IceRpc::Tests::Api::MyExceptionA", decoder), Is.Not.Null);

            Assert.That(activator.CreateInstance("::IceRpc::Tests::Api::MyExceptionB", decoder), Is.Null);
            Assert.That(activator.CreateInstance("::IceRpc::Tests::Api::MyExceptionC", decoder), Is.Null);
            Assert.That(activator.CreateInstance("::IceRpc::Tests::Api::MyExceptionD", decoder), Is.Null);

            // Create an activator that knows about A and B assemblies
            activator = Ice11Decoder.GetActivator(new Assembly[]
            {
                typeof(MyClassA).Assembly,
                typeof(MyClassB).Assembly
            });
            Assert.That(activator.CreateInstance(MyClassA.IceTypeId, decoder), Is.Not.Null);
            Assert.That(activator.CreateInstance(MyClassB.IceTypeId, decoder), Is.Not.Null);

            Assert.That(activator.CreateInstance(MyClassC.IceTypeId, decoder), Is.Null);
            Assert.That(activator.CreateInstance(MyClassD.IceTypeId, decoder), Is.Null);

            Assert.That(activator.CreateInstance("1", decoder), Is.Not.Null);
            Assert.That(activator.CreateInstance("2", decoder), Is.Not.Null);

            Assert.That(activator.CreateInstance("3", decoder), Is.Null);
            Assert.That(activator.CreateInstance("4", decoder), Is.Null);

            Assert.That(activator.CreateInstance("::IceRpc::Tests::Api::MyExceptionA", decoder), Is.Not.Null);
            Assert.That(activator.CreateInstance("::IceRpc::Tests::Api::MyExceptionB", decoder), Is.Not.Null);

            Assert.That(activator.CreateInstance("::IceRpc::Tests::Api::MyExceptionC", decoder), Is.Null);
            Assert.That(activator.CreateInstance("::IceRpc::Tests::Api::MyExceptionD", decoder), Is.Null);

            // Create an activator that knows about A, B and C assemblies
            activator = Ice11Decoder.GetActivator(new Assembly[]
            {
                typeof(MyClassA).Assembly,
                typeof(MyClassB).Assembly,
                typeof(MyClassC).Assembly
            });
            Assert.That(activator.CreateInstance(MyClassA.IceTypeId, decoder), Is.Not.Null);
            Assert.That(activator.CreateInstance(MyClassB.IceTypeId, decoder), Is.Not.Null);
            Assert.That(activator.CreateInstance(MyClassC.IceTypeId, decoder), Is.Not.Null);

            Assert.That(activator.CreateInstance(MyClassD.IceTypeId, decoder), Is.Null);

            Assert.That(activator.CreateInstance("1", decoder), Is.Not.Null);
            Assert.That(activator.CreateInstance("2", decoder), Is.Not.Null);
            Assert.That(activator.CreateInstance("3", decoder), Is.Not.Null);

            Assert.That(activator.CreateInstance("4", decoder), Is.Null);

            Assert.That(activator.CreateInstance("::IceRpc::Tests::Api::MyExceptionA", decoder), Is.Not.Null);
            Assert.That(activator.CreateInstance("::IceRpc::Tests::Api::MyExceptionB", decoder), Is.Not.Null);
            Assert.That(activator.CreateInstance("::IceRpc::Tests::Api::MyExceptionC", decoder), Is.Not.Null);

            Assert.That(activator.CreateInstance("::IceRpc::Tests::Api::MyExceptionD", decoder), Is.Null);

            // Create an activator that knows about A, B, C and D assemblies
            activator = Ice11Decoder.GetActivator(new Assembly[]
            {
                typeof(MyClassA).Assembly,
                typeof(MyClassB).Assembly,
                typeof(MyClassC).Assembly,
                typeof(MyClassD).Assembly
            });
            Assert.That(activator.CreateInstance(MyClassA.IceTypeId, decoder), Is.Not.Null);
            Assert.That(activator.CreateInstance(MyClassB.IceTypeId, decoder), Is.Not.Null);
            Assert.That(activator.CreateInstance(MyClassC.IceTypeId, decoder), Is.Not.Null);
            Assert.That(activator.CreateInstance(MyClassD.IceTypeId, decoder), Is.Not.Null);

            Assert.That(activator.CreateInstance("1", decoder), Is.Not.Null);
            Assert.That(activator.CreateInstance("2", decoder), Is.Not.Null);
            Assert.That(activator.CreateInstance("3", decoder), Is.Not.Null);
            Assert.That(activator.CreateInstance("4", decoder), Is.Not.Null);

            Assert.That(activator.CreateInstance("::IceRpc::Tests::Api::MyExceptionA", decoder), Is.Not.Null);
            Assert.That(activator.CreateInstance("::IceRpc::Tests::Api::MyExceptionB", decoder), Is.Not.Null);
            Assert.That(activator.CreateInstance("::IceRpc::Tests::Api::MyExceptionC", decoder), Is.Not.Null);
            Assert.That(activator.CreateInstance("::IceRpc::Tests::Api::MyExceptionD", decoder), Is.Not.Null);
        }
    }
}
