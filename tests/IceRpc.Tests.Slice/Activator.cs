// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Tests.ReferencedAssemblies;

using NUnit.Framework;
using System.Reflection;

namespace IceRpc.Tests.Slice
{
    [Timeout(30000)]
    public class Activator
    {
        [Test]
        public void Activator_CreateInstance()
        {
            IActivator activator = IceDecoder.GetActivator(typeof(IceDecoder).Assembly);

            var decoder = new IceDecoder(ReadOnlyMemory<byte>.Empty, Encoding.Ice11);

            Assert.That(activator.CreateInstance("::IceRpc::ServiceNotFoundException", decoder), Is.Not.Null);

            // The default activator doesn't know about types defined in separated assemblies
            Assert.That(activator.CreateInstance(ClassA.IceTypeId, decoder), Is.Null);
            Assert.That(activator.CreateInstance(ClassB.IceTypeId, decoder), Is.Null);
            Assert.That(activator.CreateInstance(ClassC.IceTypeId, decoder), Is.Null);
            Assert.That(activator.CreateInstance(ClassD.IceTypeId, decoder), Is.Null);

            Assert.That(activator.CreateInstance("1", decoder), Is.Null);
            Assert.That(activator.CreateInstance("2", decoder), Is.Null);
            Assert.That(activator.CreateInstance("3", decoder), Is.Null);
            Assert.That(activator.CreateInstance("4", decoder), Is.Null);

            Assert.That(activator.CreateInstance("::IceRpc::Tests::ReferencedAssemblies::ExceptionA", decoder), Is.Null);
            Assert.That(activator.CreateInstance("::IceRpc::Tests::ReferencedAssemblies::ExceptionB", decoder), Is.Null);
            Assert.That(activator.CreateInstance("::IceRpc::Tests::ReferencedAssemblies::ExceptionC", decoder), Is.Null);
            Assert.That(activator.CreateInstance("::IceRpc::Tests::ReferencedAssemblies::ExceptionD", decoder), Is.Null);

            activator = IceDecoder.GetActivator(typeof(ClassA).Assembly);
            Assert.That(activator.CreateInstance(ClassA.IceTypeId, decoder), Is.Not.Null);

            Assert.That(activator.CreateInstance(ClassB.IceTypeId, decoder), Is.Null);
            Assert.That(activator.CreateInstance(ClassC.IceTypeId, decoder), Is.Null);
            Assert.That(activator.CreateInstance(ClassD.IceTypeId, decoder), Is.Null);

            Assert.That(activator.CreateInstance("1", decoder), Is.Not.Null);

            Assert.That(activator.CreateInstance("2", decoder), Is.Null);
            Assert.That(activator.CreateInstance("3", decoder), Is.Null);
            Assert.That(activator.CreateInstance("4", decoder), Is.Null);

            Assert.That(activator.CreateInstance("::IceRpc::Tests::ReferencedAssemblies::ExceptionA", decoder), Is.Not.Null);

            Assert.That(activator.CreateInstance("::IceRpc::Tests::ReferencedAssemblies::ExceptionB", decoder), Is.Null);
            Assert.That(activator.CreateInstance("::IceRpc::Tests::ReferencedAssemblies::ExceptionC", decoder), Is.Null);
            Assert.That(activator.CreateInstance("::IceRpc::Tests::ReferencedAssemblies::ExceptionD", decoder), Is.Null);

            // Create an activator that knows about A and B assemblies
            activator = IceDecoder.GetActivator(new Assembly[]
            {
                typeof(ClassA).Assembly,
                typeof(ClassB).Assembly
            });
            Assert.That(activator.CreateInstance(ClassA.IceTypeId, decoder), Is.Not.Null);
            Assert.That(activator.CreateInstance(ClassB.IceTypeId, decoder), Is.Not.Null);

            Assert.That(activator.CreateInstance(ClassC.IceTypeId, decoder), Is.Null);
            Assert.That(activator.CreateInstance(ClassD.IceTypeId, decoder), Is.Null);

            Assert.That(activator.CreateInstance("1", decoder), Is.Not.Null);
            Assert.That(activator.CreateInstance("2", decoder), Is.Not.Null);

            Assert.That(activator.CreateInstance("3", decoder), Is.Null);
            Assert.That(activator.CreateInstance("4", decoder), Is.Null);

            Assert.That(activator.CreateInstance("::IceRpc::Tests::ReferencedAssemblies::ExceptionA", decoder), Is.Not.Null);
            Assert.That(activator.CreateInstance("::IceRpc::Tests::ReferencedAssemblies::ExceptionB", decoder), Is.Not.Null);

            Assert.That(activator.CreateInstance("::IceRpc::Tests::ReferencedAssemblies::ExceptionC", decoder), Is.Null);
            Assert.That(activator.CreateInstance("::IceRpc::Tests::ReferencedAssemblies::ExceptionD", decoder), Is.Null);

            // Create an activator that knows about A, B and C assemblies
            activator = IceDecoder.GetActivator(new Assembly[]
            {
                typeof(ClassA).Assembly,
                typeof(ClassB).Assembly,
                typeof(ClassC).Assembly
            });
            Assert.That(activator.CreateInstance(ClassA.IceTypeId, decoder), Is.Not.Null);
            Assert.That(activator.CreateInstance(ClassB.IceTypeId, decoder), Is.Not.Null);
            Assert.That(activator.CreateInstance(ClassC.IceTypeId, decoder), Is.Not.Null);

            Assert.That(activator.CreateInstance(ClassD.IceTypeId, decoder), Is.Null);

            Assert.That(activator.CreateInstance("1", decoder), Is.Not.Null);
            Assert.That(activator.CreateInstance("2", decoder), Is.Not.Null);
            Assert.That(activator.CreateInstance("3", decoder), Is.Not.Null);

            Assert.That(activator.CreateInstance("4", decoder), Is.Null);

            Assert.That(activator.CreateInstance("::IceRpc::Tests::ReferencedAssemblies::ExceptionA", decoder), Is.Not.Null);
            Assert.That(activator.CreateInstance("::IceRpc::Tests::ReferencedAssemblies::ExceptionB", decoder), Is.Not.Null);
            Assert.That(activator.CreateInstance("::IceRpc::Tests::ReferencedAssemblies::ExceptionC", decoder), Is.Not.Null);

            Assert.That(activator.CreateInstance("::IceRpc::Tests::ReferencedAssemblies::ExceptionD", decoder), Is.Null);

            // Create an activator that knows about A, B, C and D assemblies
            activator = IceDecoder.GetActivator(new Assembly[]
            {
                typeof(ClassA).Assembly,
                typeof(ClassB).Assembly,
                typeof(ClassC).Assembly,
                typeof(ClassD).Assembly
            });
            Assert.That(activator.CreateInstance(ClassA.IceTypeId, decoder), Is.Not.Null);
            Assert.That(activator.CreateInstance(ClassB.IceTypeId, decoder), Is.Not.Null);
            Assert.That(activator.CreateInstance(ClassC.IceTypeId, decoder), Is.Not.Null);
            Assert.That(activator.CreateInstance(ClassD.IceTypeId, decoder), Is.Not.Null);

            Assert.That(activator.CreateInstance("1", decoder), Is.Not.Null);
            Assert.That(activator.CreateInstance("2", decoder), Is.Not.Null);
            Assert.That(activator.CreateInstance("3", decoder), Is.Not.Null);
            Assert.That(activator.CreateInstance("4", decoder), Is.Not.Null);

            Assert.That(activator.CreateInstance("::IceRpc::Tests::ReferencedAssemblies::ExceptionA", decoder), Is.Not.Null);
            Assert.That(activator.CreateInstance("::IceRpc::Tests::ReferencedAssemblies::ExceptionB", decoder), Is.Not.Null);
            Assert.That(activator.CreateInstance("::IceRpc::Tests::ReferencedAssemblies::ExceptionC", decoder), Is.Not.Null);
            Assert.That(activator.CreateInstance("::IceRpc::Tests::ReferencedAssemblies::ExceptionD", decoder), Is.Not.Null);
        }
    }
}
