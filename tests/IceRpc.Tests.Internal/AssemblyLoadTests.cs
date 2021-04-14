// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace IceRpc.Tests.Internal
{
    [Parallelizable(scope: ParallelScope.All)]
    public class AssemblyLoadTests
    {
        [Test]
        public void AssemblyLoad_FindFactory()
        {
            // MyClassA here ensure that reference to A.dll is kept
            var a = new MyClassA("aValue");
            Assert.AreEqual("aValue", a.AValue);

            // A.dll is already loaded because MyClassA type is used above
            Assert.That(Runtime.TypeIdClassFactoryCache.ContainsKey("::IceRpc::Tests::Internal::MyClassA"), Is.True);

            // B, is not loaded because it is not referenced anywhere
            Assert.That(Runtime.TypeIdClassFactoryCache.ContainsKey("::IceRpc::Tests::Internal::MyClassB"), Is.False);
            Assert.That(Runtime.TypeIdClassFactoryCache.ContainsKey("::IceRpc::Tests::Internal::MyClassC"), Is.False);
            Assert.That(Runtime.TypeIdClassFactoryCache.ContainsKey("::IceRpc::Tests::Internal::MyClassD"), Is.False);

            RegisterClassFactoriesFromAssembly("D.dll");
            // After loading D MyClassD is found, MyClassC and MyClassB are still not found because
            // RegisterClassFactoriesFromAssembly only load factories from the specified assembly and not from its
            // referenced assemblies.
            Assert.That(Runtime.TypeIdClassFactoryCache.ContainsKey("::IceRpc::Tests::Internal::MyClassD"), Is.True);
            Assert.That(Runtime.TypeIdClassFactoryCache.ContainsKey("::IceRpc::Tests::Internal::MyClassB"), Is.False);
            Assert.That(Runtime.TypeIdClassFactoryCache.ContainsKey("::IceRpc::Tests::Internal::MyClassC"), Is.False);

            // Now load B and C
            RegisterClassFactoriesFromAssembly("B.dll");
            Assert.That(Runtime.TypeIdClassFactoryCache.ContainsKey("::IceRpc::Tests::Internal::MyClassB"), Is.True);
            Assert.That(Runtime.TypeIdClassFactoryCache.ContainsKey("::IceRpc::Tests::Internal::MyClassC"), Is.False);

            RegisterClassFactoriesFromAssembly("C.dll");
            Assert.That(Runtime.TypeIdClassFactoryCache.ContainsKey("::IceRpc::Tests::Internal::MyClassC"), Is.True);

            static void RegisterClassFactoriesFromAssembly(string name)
            {
                string path = Path.Combine(Directory.GetCurrentDirectory(), name);
                // Load assembly D
                Assembly assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
                Assert.IsNotNull(assembly);
                Runtime.RegisterClassFactoriesFromAssembly(assembly);
            }
        }
    }
}
