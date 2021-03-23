// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System.IO;
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
            Assert.IsNotNull(Runtime.FindClassFactory("::IceRpc::Tests::Internal::MyClassA"));

            // B, is not loaded because it is not referenced anywhere
            Assert.IsNull(Runtime.FindClassFactory("::IceRpc::Tests::Internal::MyClassB"));

            //Assert.IsNull(Runtime.FindClassFactory("::IceRpc::Tests::Internal::MyClassC"));
            //Assert.IsNull(Runtime.FindClassFactory("::IceRpc::Tests::Internal::MyClassD"));

            var path = Path.Combine(Directory.GetCurrentDirectory(), "D.dll");
            // Load assembly D
            var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
            Assert.IsNotNull(assembly);
            Runtime.RegisterFactoriesFromAssembly(assembly);

            // After loading D MyClassD is found, MyClassC too because C is a direct
            // dependency of D, and MyClassB cannot be load because the previous failure laoding
            // MyClassB causes a null factory to be cached.

            // B factory is still null because the runtime cache the previous failure
            Assert.IsNull(Runtime.FindClassFactory("::IceRpc::Tests::Internal::MyClassB"));
            Assert.IsNotNull(Runtime.FindClassFactory("::IceRpc::Tests::Internal::MyClassC"));
            Assert.IsNotNull(Runtime.FindClassFactory("::IceRpc::Tests::Internal::MyClassD"));
        }
    }
}
