// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.CodeGeneration
{
    [Timeout(30000)]
    public sealed class InterfaceInheritanceTests : IAsyncDisposable
    {
        private readonly Connection _connection;
        private readonly Server _server;
        private readonly IMyInterfaceBasePrx _basePrx;
        private readonly IMyInterfaceDerivedPrx _derivedPrx;
        private readonly IMyInterfaceMostDerivedPrx _mostDerivedPrx;

        public InterfaceInheritanceTests()
        {
            _connection = new Connection();

            var router = new Router();
            router.Map<IMyInterfaceBase>(new Base());
            router.Map<IMyInterfaceDerived>(new Derived());
            router.Map<IMyInterfaceMostDerived>(new MostDerived());

            _server = new Server
            {
                Dispatcher = router,
                Endpoint = TestHelper.GetUniqueColocEndpoint()
            };
            _server.Listen();
            _connection = new Connection { RemoteEndpoint = _server.ProxyEndpoint };
            _basePrx = IMyInterfaceBasePrx.FromConnection(_connection);
            _derivedPrx = IMyInterfaceDerivedPrx.FromConnection(_connection);
            _mostDerivedPrx = IMyInterfaceMostDerivedPrx.FromConnection(_connection);
        }

        [OneTimeTearDown]
        public async Task TearDownAsync() => await DisposeAsync();

        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Structure",
            "NUnit1028:The non-test method is public",
            Justification = "IAsyncDispoable implementation")]
        public async ValueTask DisposeAsync()
        {
            await _server.DisposeAsync();
            await _connection.DisposeAsync();
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

        [Test]
        public void InterfaceInheritance_Types()
        {
            Assert.That(typeof(IMyInterfaceBasePrx).IsAssignableFrom(typeof(IMyInterfaceDerivedPrx)), Is.True);
            Assert.That(typeof(IMyInterfaceBasePrx).IsAssignableFrom(typeof(IMyInterfaceMostDerivedPrx)), Is.True);
            Assert.That(typeof(IMyInterfaceDerivedPrx).IsAssignableFrom(typeof(IMyInterfaceMostDerivedPrx)),
                        Is.True);

            Assert.That(typeof(IMyInterfaceBase).IsAssignableFrom(typeof(IMyInterfaceDerived)), Is.True);
            Assert.That(typeof(IMyInterfaceBase).IsAssignableFrom(typeof(IMyInterfaceMostDerived)), Is.True);
            Assert.That(typeof(IMyInterfaceDerived).IsAssignableFrom(typeof(IMyInterfaceMostDerived)),
                        Is.True);
        }

        public class Base : IMyInterfaceBase
        {
            public ValueTask OpBaseAsync(Dispatch dispatch, CancellationToken cancel) => default;
        }

        public class Derived : Base, IMyInterfaceDerived
        {
            public ValueTask OpDerivedAsync(Dispatch dispatch, CancellationToken cancel) => default;
        }

        public class MostDerived : Derived, IMyInterfaceMostDerived
        {
            public ValueTask OpMostDerivedAsync(Dispatch dispatch, CancellationToken cancel) => default;
        }
    }
}
