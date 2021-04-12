// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.CodeGeneration
{
    [Timeout(30000)]
    public class InterfaceInheritanceTests
    {
        private readonly Communicator _communicator;
        private readonly Server _server;
        private readonly IMyInterfaceBasePrx _basePrx;
        private readonly IMyInterfaceDerivedPrx _derivedPrx;
        private readonly IMyInterfaceMostDerivedPrx _mostDerivedPrx;

        public InterfaceInheritanceTests()
        {
            _communicator = new Communicator();
            _server = new Server { Communicator = _communicator};
            _ = _server.ListenAndServeAsync();
            _basePrx = _server.Add("/base", new Base(), IMyInterfaceBasePrx.Factory);
            _derivedPrx = _server.Add("/derived", new Derived(), IMyInterfaceDerivedPrx.Factory);
            _mostDerivedPrx = _server.Add("/mostderived", new MostDerived(), IMyInterfaceMostDerivedPrx.Factory);
        }

        [OneTimeTearDown]
        public async Task TearDownAsync()
        {
            await _server.DisposeAsync();
            await _communicator.DisposeAsync();
        }

        [Test]
        public async Task InterfaceInheritance_IceIsAAsync()
        {
            Assert.That(await _basePrx.IceIsAAsync("::IceRpc::Tests::CodeGeneration::MyInterfaceBase"),
                        Is.True);
            Assert.That(await _basePrx.IceIsAAsync("::IceRpc::Tests::CodeGeneration::MyInterfaceDerived"),
                        Is.False);
            Assert.That(await _basePrx.IceIsAAsync("::IceRpc::Tests::CodeGeneration::MyInterfaceMostDerived"),
                        Is.False);

            Assert.That(await _derivedPrx.IceIsAAsync("::IceRpc::Tests::CodeGeneration::MyInterfaceBase"),
                        Is.True);
            Assert.That(await _derivedPrx.IceIsAAsync("::IceRpc::Tests::CodeGeneration::MyInterfaceDerived"),
                        Is.True);
            Assert.That(await _derivedPrx.IceIsAAsync("::IceRpc::Tests::CodeGeneration::MyInterfaceMostDerived"),
                        Is.False);

            Assert.That(await _mostDerivedPrx.IceIsAAsync("::IceRpc::Tests::CodeGeneration::MyInterfaceBase"),
                        Is.True);
            Assert.That(await _mostDerivedPrx.IceIsAAsync("::IceRpc::Tests::CodeGeneration::MyInterfaceDerived"),
                        Is.True);
            Assert.That(await _mostDerivedPrx.IceIsAAsync("::IceRpc::Tests::CodeGeneration::MyInterfaceMostDerived"),
                        Is.True);
        }

        [Test]
        public async Task InterfaceInheritance_IceIdAsync()
        {
            Assert.AreEqual("::IceRpc::Tests::CodeGeneration::MyInterfaceBase", await _basePrx.IceIdAsync());
            Assert.AreEqual("::IceRpc::Tests::CodeGeneration::MyInterfaceDerived", await _derivedPrx.IceIdAsync());
            Assert.AreEqual("::IceRpc::Tests::CodeGeneration::MyInterfaceMostDerived",
                            await _mostDerivedPrx.IceIdAsync());
        }

        [Test]
        public async Task InterfaceInheritance_IceIdsAsync()
        {
            CollectionAssert.AreEqual(
                new string[]
                {
                    "::Ice::Object",
                    "::IceRpc::Tests::CodeGeneration::MyInterfaceBase"
                },
                await _basePrx.IceIdsAsync());

            CollectionAssert.AreEqual(
                new string[]
                {
                    "::Ice::Object",
                    "::IceRpc::Tests::CodeGeneration::MyInterfaceBase",
                    "::IceRpc::Tests::CodeGeneration::MyInterfaceDerived",
                },
                await _derivedPrx.IceIdsAsync());

            CollectionAssert.AreEqual(
                new string[]
                {
                    "::Ice::Object",
                    "::IceRpc::Tests::CodeGeneration::MyInterfaceBase",
                    "::IceRpc::Tests::CodeGeneration::MyInterfaceDerived",
                    "::IceRpc::Tests::CodeGeneration::MyInterfaceMostDerived",
                },
                await _mostDerivedPrx.IceIdsAsync());
        }

        [Test]
        public async Task InterfaceInheritance_OperationsAsync()
        {
            await _basePrx.OpBaseAsync();

            await _derivedPrx.OpBaseAsync();
            await _derivedPrx.OpDerivedAsync();

            await _mostDerivedPrx.OpBaseAsync();
            await _mostDerivedPrx.OpDerivedAsync();
            await _mostDerivedPrx.OpMostDerivedAsync();
        }

        public class Base : IAsyncMyInterfaceBase
        {
            public ValueTask OpBaseAsync(Current current, CancellationToken cancel) => default;
        }

        public class Derived : Base, IAsyncMyInterfaceDerived
        {
            public ValueTask OpDerivedAsync(Current current, CancellationToken cancel) => default;
        }

        public class MostDerived : Derived, IAsyncMyInterfaceMostDerived
        {
            public ValueTask OpMostDerivedAsync(Current current, CancellationToken cancel) => default;
        }
    }
}
