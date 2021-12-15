// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Tests.Slice.InterfaceInheritance;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Tests.Slice
{
    [Timeout(30000)]
    public sealed class InterfaceInheritanceTests
    {
        private readonly ServiceProvider _serviceProvider;
        private readonly APrx _aPrx;
        private readonly BPrx _bPrx;
        private readonly CPrx _cPrx;
        private readonly DPrx _dPrx;

        public InterfaceInheritanceTests()
        {
            _serviceProvider = new IntegrationTestServiceCollection()
                .AddTransient<IDispatcher>(_ => {
                    var router = new Router();
                    router.Map<IA>(new A());
                    router.Map<IB>(new B());
                    router.Map<IC>(new C());
                    router.Map<ID>(new D());
                    return router;
                })
                .BuildServiceProvider();

            Connection connection = _serviceProvider.GetRequiredService<Connection>();
            _aPrx = APrx.FromConnection(connection);
            _bPrx = BPrx.FromConnection(connection);
            _cPrx = CPrx.FromConnection(connection);
            _dPrx = DPrx.FromConnection(connection);
        }

        [OneTimeTearDown]
        public ValueTask DisposeAsync() => _serviceProvider.DisposeAsync();

        [Test]
        public async Task InterfaceInheritance_IceIsAAsync()
        {
            Assert.That(await _aPrx.IceIsAAsync("::IceRpc::Tests::Slice::InterfaceInheritance::A"),
                        Is.True);
            Assert.That(await _aPrx.IceIsAAsync("::IceRpc::Tests::Slice::InterfaceInheritance::B"),
                        Is.False);
            Assert.That(await _aPrx.IceIsAAsync("::IceRpc::Tests::Slice::InterfaceInheritance::D"),
                        Is.False);

            Assert.That(await _bPrx.IceIsAAsync("::IceRpc::Tests::Slice::InterfaceInheritance::A"),
                        Is.True);
            Assert.That(await _bPrx.IceIsAAsync("::IceRpc::Tests::Slice::InterfaceInheritance::B"),
                        Is.True);
            Assert.That(await _bPrx.IceIsAAsync("::IceRpc::Tests::Slice::InterfaceInheritance::D"),
                        Is.False);

            Assert.That(await _cPrx.IceIsAAsync("::IceRpc::Tests::Slice::InterfaceInheritance::A"),
                        Is.True);
            Assert.That(await _cPrx.IceIsAAsync("::IceRpc::Tests::Slice::InterfaceInheritance::C"),
                        Is.True);
            Assert.That(await _cPrx.IceIsAAsync("::IceRpc::Tests::Slice::InterfaceInheritance::D"),
                        Is.False);

            Assert.That(await _dPrx.IceIsAAsync("::IceRpc::Tests::Slice::InterfaceInheritance::A"),
                        Is.True);
            Assert.That(await _dPrx.IceIsAAsync("::IceRpc::Tests::Slice::InterfaceInheritance::B"),
                        Is.True);
            Assert.That(await _dPrx.IceIsAAsync("::IceRpc::Tests::Slice::InterfaceInheritance::D"),
                        Is.True);
        }

        [Test]
        public async Task InterfaceInheritance_IceIdsAsync()
        {
            CollectionAssert.AreEqual(
                new string[]
                {
                    "::IceRpc::Service",
                    "::IceRpc::Tests::Slice::InterfaceInheritance::A"
                },
                await _aPrx.IceIdsAsync());

            CollectionAssert.AreEqual(
                new string[]
                {
                    "::IceRpc::Service",
                    "::IceRpc::Tests::Slice::InterfaceInheritance::A",
                    "::IceRpc::Tests::Slice::InterfaceInheritance::B",
                },
                await _bPrx.IceIdsAsync());

            CollectionAssert.AreEqual(
                new string[]
                {
                    "::IceRpc::Service",
                    "::IceRpc::Tests::Slice::InterfaceInheritance::A",
                    "::IceRpc::Tests::Slice::InterfaceInheritance::B",
                    "::IceRpc::Tests::Slice::InterfaceInheritance::C",
                    "::IceRpc::Tests::Slice::InterfaceInheritance::D",
                },
                await _dPrx.IceIdsAsync());
        }

        [Test]
        public async Task InterfaceInheritance_OperationsAsync()
        {
            DPrx d = await _aPrx.OpAAsync(_aPrx);

            d = await _bPrx.OpAAsync(d);
            _ = await _bPrx.OpBAsync(d);

            _ = await _dPrx.OpAAsync(d);
            _ = await _dPrx.OpBAsync(d);
            _ = await _dPrx.OpCAsync(d);
            _ = await _dPrx.OpDAsync(d);
        }

        [Test]
        public void InterfaceInheritance_Types()
        {
            Assert.That(typeof(IAPrx).IsAssignableFrom(typeof(IBPrx)), Is.True);
            Assert.That(typeof(IAPrx).IsAssignableFrom(typeof(IDPrx)), Is.True);
            Assert.That(typeof(IBPrx).IsAssignableFrom(typeof(IDPrx)), Is.True);
            Assert.That(typeof(ICPrx).IsAssignableFrom(typeof(IDPrx)), Is.True);

            Assert.That(typeof(IServicePrx).IsAssignableFrom(typeof(IBPrx)), Is.False);
            Assert.That(typeof(IServicePrx).IsAssignableFrom(typeof(ICPrx)), Is.True);

            Assert.That(typeof(IA).IsAssignableFrom(typeof(IB)), Is.True);
            Assert.That(typeof(IA).IsAssignableFrom(typeof(IC)), Is.True);
            Assert.That(typeof(IB).IsAssignableFrom(typeof(ID)), Is.True);
            Assert.That(typeof(IC).IsAssignableFrom(typeof(ID)), Is.True);

            Assert.That(typeof(IService).IsAssignableFrom(typeof(IB)), Is.False);
            Assert.That(typeof(IService).IsAssignableFrom(typeof(ID)), Is.True);
        }

        public class A : Service, IA
        {
            public ValueTask<DPrx> OpAAsync(
                APrx p,
                Dispatch dispatch,
                CancellationToken cancel) => new(new DPrx(Proxy.FromPath(p.Proxy.Path)));
        }

        public class B : A, IB
        {
            public ValueTask<BPrx> OpBAsync(
                BPrx p,
                Dispatch dispatch,
                CancellationToken cancel) => new(new BPrx(Proxy.FromPath(dispatch.Path)));
        }

        public class C : A, IC
        {
            public ValueTask<CPrx> OpCAsync(
                CPrx p,
                Dispatch dispatch,
                CancellationToken cancel) => new(new CPrx(Proxy.FromPath(dispatch.Path)));
        }

        public class D : B, ID
        {
            // Need implementation for C
            public ValueTask<CPrx> OpCAsync(
                CPrx p,
                Dispatch dispatch,
                CancellationToken cancel) => new(new CPrx(Proxy.FromPath(dispatch.Path)));

            public ValueTask<APrx> OpDAsync(
                DPrx p,
                Dispatch dispatch,
                CancellationToken cancel) => new(new DPrx(Proxy.FromPath(dispatch.Path)));
        }
    }
}
