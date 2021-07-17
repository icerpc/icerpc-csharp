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
        private readonly MyInterfaceBasePrx _basePrx;
        private readonly MyInterfaceDerivedPrx _derivedPrx;
        private readonly MyInterfaceMostDerivedPrx _mostDerivedPrx;

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
            _basePrx = MyInterfaceBasePrx.FromConnection(_connection);
            _derivedPrx = MyInterfaceDerivedPrx.FromConnection(_connection);
            _mostDerivedPrx = MyInterfaceMostDerivedPrx.FromConnection(_connection);
        }

        [OneTimeTearDown]
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
        public async Task InterfaceInheritance_IceIdsAsync()
        {
            CollectionAssert.AreEqual(
                new string[]
                {
                    "::IceRpc::Service",
                    "::IceRpc::Tests::CodeGeneration::MyInterfaceBase"
                },
                await _basePrx.IceIdsAsync());

            CollectionAssert.AreEqual(
                new string[]
                {
                    "::IceRpc::Service",
                    "::IceRpc::Tests::CodeGeneration::MyInterfaceBase",
                    "::IceRpc::Tests::CodeGeneration::MyInterfaceDerived",
                },
                await _derivedPrx.IceIdsAsync());

            CollectionAssert.AreEqual(
                new string[]
                {
                    "::IceRpc::Service",
                    "::IceRpc::Tests::CodeGeneration::MyInterfaceBase",
                    "::IceRpc::Tests::CodeGeneration::MyInterfaceDerived",
                    "::IceRpc::Tests::CodeGeneration::MyInterfaceMostDerived",
                },
                await _mostDerivedPrx.IceIdsAsync());
        }

        [Test]
        public async Task InterfaceInheritance_OperationsAsync()
        {
            MyInterfaceMostDerivedPrx mostDerived = await _basePrx.OpBaseAsync(_basePrx);

            mostDerived = await _derivedPrx.OpBaseAsync(mostDerived);
            MyInterfaceBasePrx basePrx = await _derivedPrx.OpDerivedAsync(mostDerived);

            basePrx = await _mostDerivedPrx.OpBaseAsync(mostDerived);
            basePrx = await _mostDerivedPrx.OpDerivedAsync(mostDerived);
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

        public class Base : Service, IMyInterfaceBase
        {
            public ValueTask<MyInterfaceMostDerivedPrx> OpBaseAsync(
                MyInterfaceBasePrx p,
                Dispatch dispatch,
                CancellationToken cancel) => new(new MyInterfaceMostDerivedPrx(new Proxy(p.Proxy.Path)));
        }

        public class Derived : Base, IMyInterfaceDerived
        {
            public ValueTask<MyInterfaceBasePrx> OpDerivedAsync(
                MyInterfaceMostDerivedPrx p,
                Dispatch dispatch,
                CancellationToken cancel) => new(new MyInterfaceMostDerivedPrx(new Proxy(dispatch.Path)));
        }

        public class MostDerived : Derived, IMyInterfaceMostDerived
        {
            public ValueTask OpMostDerivedAsync(Dispatch dispatch, CancellationToken cancel) => default;
        }
    }
}
