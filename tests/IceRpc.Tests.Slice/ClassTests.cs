// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Slice;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Tests.Slice
{
    [Timeout(30000)]
    [Parallelizable(ParallelScope.All)]
    [TestFixture(ProtocolCode.Ice)]
    [TestFixture(ProtocolCode.IceRpc)]
    public sealed class ClassTests
    {
        private readonly ServiceProvider _serviceProvider;
        private readonly ClassOperationsPrx _prx;
        private readonly ClassOperationsUnexpectedClassPrx _prxUnexpectedClass;

        public ClassTests(ProtocolCode protocol)
        {
            _serviceProvider = new IntegrationTestServiceCollection()
                .UseProtocol(protocol)
                .AddTransient<IDispatcher>(_ =>
                    {
                        var router = new Router();
                        router.Map<IClassOperations>(new ClassOperations());
                        router.Map<IClassOperationsUnexpectedClass>(new InlineDispatcher(
                            (request, cancel) =>
                            {
                                var response = new OutgoingResponse(request)
                                {
                                    PayloadSource = Encoding.Ice11.CreatePayloadFromSingleReturnValue(
                                        new MyClassAlsoEmpty(),
                                        (ref IceEncoder encoder, MyClassAlsoEmpty ae) => encoder.EncodeClass(ae))
                                };
                                return new(response);
                            }));
                        return router;
                    })
                .BuildServiceProvider();

            Connection connection = _serviceProvider.GetRequiredService<Connection>();
            _prx = ClassOperationsPrx.FromConnection(connection);
            _prx.Proxy.Encoding = Encoding.Ice11; // TODO: should not be necessary
            _prxUnexpectedClass = ClassOperationsUnexpectedClassPrx.FromConnection(connection);
            _prxUnexpectedClass.Proxy.Encoding = Encoding.Ice11;
        }

        [OneTimeTearDown]
        public ValueTask DisposeAsync() => _serviceProvider.DisposeAsync();

        [Test]
        public async Task Class_OperationsAsync()
        {
            MyClassB? b1 = await _prx.GetB1Async();
            Assert.That(b1, Is.Not.Null);

            MyClassB? b2 = await _prx.GetB2Async();
            Assert.That(b2, Is.Not.Null);

            MyClassC? c = await _prx.GetCAsync();
            Assert.That(c, Is.Not.Null);

            MyClassD? d = await _prx.GetDAsync();
            Assert.That(d, Is.Not.Null);

            Assert.AreNotEqual(b1, b2);
            Assert.AreEqual(b1.TheB, b1);
            Assert.That(b1.TheC, Is.Null);
            Assert.That(b1.TheA, Is.InstanceOf<MyClassB>());
            Assert.AreEqual(((MyClassB)b1.TheA!).TheA, b1.TheA);
            Assert.AreEqual(((MyClassB)b1.TheA!).TheB, b1);
            Assert.AreEqual(((MyClassB)b1.TheA).TheC!.TheB, b1.TheA);

            Assert.AreEqual(b2.TheA, b2);
            Assert.That(d.TheC, Is.Null);

            (MyClassB? r1, MyClassB? r2, MyClassC? r3, MyClassD? r4) = await _prx.GetAllAsync();
            Assert.That(r1, Is.Not.Null);
            Assert.That(r2, Is.Not.Null);
            Assert.That(r3, Is.Not.Null);
            Assert.That(r4, Is.Not.Null);

            Assert.AreNotEqual(r1, r2);
            Assert.AreEqual(r1.TheA, r2);
            Assert.AreEqual(r1.TheB, r1);
            Assert.That(r1.TheC, Is.Null);
            Assert.AreEqual(r2.TheA, r2);
            Assert.AreEqual(r2.TheB, r1);
            Assert.AreEqual(r2.TheC, r3);
            Assert.AreEqual(r3.TheB, r2);
            Assert.AreEqual(r4.TheA, r1);
            Assert.AreEqual(r4.TheB, r2);
            Assert.That(r4.TheC, Is.Null);

            MyClassK? k = await _prx.GetKAsync();
            var l = k!.Value as MyClassL;
            Assert.That(l, Is.Not.Null);
            Assert.AreEqual("l", l!.Data);

            MyClassD1? d1 = await _prx.GetD1Async(
                new MyClassD1(
                    new MyClassA1("a1"),
                    new MyClassA1("a2"),
                    new MyClassA1("a3"),
                    new MyClassA1("a4")));
            Assert.AreEqual("a1", d1!.A1!.Name);
            Assert.AreEqual("a2", d1!.A2!.Name);
            Assert.AreEqual("a3", d1!.A3!.Name);
            Assert.AreEqual("a4", d1!.A4!.Name);

            if (_prx.Proxy.Encoding == Encoding.Ice11)
            {
                MyDerivedException? ex = Assert.ThrowsAsync<MyDerivedException>(
                    async () => await _prx.ThrowMyDerivedExceptionAsync());
                Assert.AreEqual("a1", ex!.A1!.Name);
                Assert.AreEqual("a2", ex!.A2!.Name);
                Assert.AreEqual("a3", ex!.A3!.Name);
                Assert.AreEqual("a4", ex!.A4!.Name);
            }
            else if (_prx.Proxy.Encoding == Encoding.Ice20)
            {
                // The method throws an exception with classes that gets sliced to the first 2.0-encodable base class,
                // RemoteException.
                Assert.ThrowsAsync<RemoteException>(async () => await _prx.ThrowMyDerivedExceptionAsync());
            }

            (MyClassE e1, MyClassE e2) =
                await _prx.OpEAsync(new MyClassE(theB: new MyClassB(), theC: new MyClassC()), 42);
            Assert.That(e1, Is.Not.Null);
            Assert.That(e1.TheB, Is.InstanceOf<MyClassB>());
            Assert.That(e1.TheC, Is.InstanceOf<MyClassC>());

            Assert.That(e2, Is.Not.Null);
            Assert.That(e2.TheB, Is.InstanceOf<MyClassB>());
            Assert.That(e2.TheC, Is.InstanceOf<MyClassC>());
        }

        [Test]
        public async Task Class_WithComplexDictionaryAsync()
        {
            var d = new Dictionary<MyStruct, MyClassL>();

            var k1 = new MyStruct(1, 1);
            d[k1] = new MyClassL("one");

            var k2 = new MyStruct(2, 2);
            d[k2] = new MyClassL("two");

            (MyClassM m2, MyClassM m1) = await _prx.OpMAsync(new MyClassM(d));

            Assert.That(m1, Is.Not.Null);
            Assert.AreEqual(2, m1.V.Count);
            Assert.AreEqual("one", m1.V[k1].Data);
            Assert.AreEqual("two", m1.V[k2].Data);

            Assert.That(m2, Is.Not.Null);
            Assert.AreEqual(2, m2.V.Count);
            Assert.AreEqual("one", m2.V[k1].Data);
            Assert.AreEqual("two", m2.V[k2].Data);
        }

        [Test]
        public async Task Class_AnyClassParameterAsync()
        {
            // testing AnyClass as parameter
            {
                (AnyClass? v3, AnyClass? v2) = await _prx.OpClassAsync(new MyClassL("l"));
                Assert.AreEqual("l", ((MyClassL)v2!).Data);
                Assert.AreEqual("l", ((MyClassL)v3!).Data);
            }

            {
                (AnyClass?[] v3, AnyClass?[] v2) = await _prx.OpClassSeqAsync(new AnyClass[] { new MyClassL("l") });
                Assert.AreEqual("l", ((MyClassL)v2[0]!).Data);
                Assert.AreEqual("l", ((MyClassL)v3[0]!).Data);
            }

            {
                var v1 = new Dictionary<string, AnyClass?> { { "l", new MyClassL("l") } };
                (Dictionary<string, AnyClass?> v3, Dictionary<string, AnyClass?> v2) = await _prx.OpClassMapAsync(v1);
                Assert.AreEqual("l", ((MyClassL)v2["l"]!).Data);
                Assert.AreEqual("l", ((MyClassL)v3["l"]!).Data);
            }
        }

        [Test]
        public void Class_UnexpectedClass() =>
            Assert.ThrowsAsync<InvalidDataException>(async () => await _prxUnexpectedClass.OpAsync());

        [Test]
        public async Task Class_CompactIdAsync() =>
            Assert.That(await _prx.GetCompactAsync(), Is.Not.Null);

        [Test]
        public async Task Class_RecursiveTypeAsync()
        {
            // testing recursive type
            var top = new MyClassRecursive();
            MyClassRecursive p = top;
            int depth = 0;
            try
            {
                for (; depth <= 1000; ++depth)
                {
                    p.V = new MyClassRecursive();
                    p = p.V;
                    if ((depth < 10 && (depth % 10) == 0) ||
                        (depth < 1000 && (depth % 100) == 0) ||
                        (depth < 10000 && (depth % 1000) == 0) ||
                        (depth % 10000) == 0)
                    {
                        await _prx.OpRecursiveAsync(top);
                    }
                }
                Assert.Fail();
            }
            catch (UnhandledException)
            {
                // Expected marshal exception from the server (max class graph depth reached)
            }
        }

        public class ClassOperations : Service, IClassOperations
        {
            private readonly MyClassB _b1;
            private readonly MyClassB _b2;
            private readonly MyClassC _c;
            private readonly MyClassD _d;

            public ClassOperations()
            {
                _b1 = new MyClassB();
                _b2 = new MyClassB();
                _c = new MyClassC();
                _d = new MyClassD();

                _b1.TheA = _b2; // Cyclic reference to another B
                _b1.TheB = _b1; // Self reference.
                _b1.TheC = null; // Null reference.

                _b2.TheA = _b2; // Self reference, using base.
                _b2.TheB = _b1; // Cyclic reference to another B
                _b2.TheC = _c; // Cyclic reference to a C.

                _c.TheB = _b2; // Cyclic reference to a B.

                _d.TheA = _b1; // Reference to a B.
                _d.TheB = _b2; // Reference to a B.
                _d.TheC = null; // Reference to a C.
            }

            public ValueTask<(MyClassB R1, MyClassB R2, MyClassC R3, MyClassD R4)> GetAllAsync(
                Dispatch dispatch,
                CancellationToken cancel) => new((_b1, _b2, _c, _d));

            public ValueTask<MyClassB> GetB1Async(Dispatch dispatch, CancellationToken cancel) => new(_b1);

            public ValueTask<MyClassB> GetB2Async(Dispatch dispatch, CancellationToken cancel) => new(_b2);

            public ValueTask<MyClassC> GetCAsync(Dispatch dispatch, CancellationToken cancel) => new(_c);

            public ValueTask<MyCompactClass> GetCompactAsync(Dispatch dispatch, CancellationToken cancel) =>
                new(new MyDerivedCompactClass());

            public ValueTask<MyClassD1> GetD1Async(MyClassD1 p1, Dispatch dispatch, CancellationToken cancel) =>
                new(p1);

            public ValueTask<MyClassD> GetDAsync(Dispatch dispatch, CancellationToken cancel) => new(_d);

            public ValueTask<MyClassK> GetKAsync(Dispatch dispatch, CancellationToken cancel) =>
                new(new MyClassK(new MyClassL("l")));
            public ValueTask<MyClass2> GetMyClass2Async(Dispatch dispatch, CancellationToken cancel) =>
                new(new MyClass2());
            public ValueTask<MyDerivedClass1> GetMyDerivedClass1Async(Dispatch dispatch, CancellationToken cancel)
            {
                // We don't pass the values in the constructor because the partial initialize implementation overrides them
                // for testing purpose.
                var derived = new MyDerivedClass1("", "");
                derived.Id = "Other id";
                derived.Name = "Other name";
                return new(derived);
            }
            public ValueTask<MyDerivedClass2> GetMyDerivedClass2Async(Dispatch dispatch, CancellationToken cancel)
            {
                // We don't pass the values in the constructor because the partial initialize implementation overrides them
                // for testing purpose.
                var derived = new MyDerivedClass2("");
                derived.Id = "Other id";
                return new(derived);
            }
            public ValueTask<(AnyClass? R1, AnyClass? R2)> OpClassAsync(
                AnyClass? p1,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p1));

            public ValueTask<(IEnumerable<KeyValuePair<string, AnyClass?>> R1, IEnumerable<KeyValuePair<string, AnyClass?>> R2)> OpClassMapAsync(
                Dictionary<string, AnyClass?> p1,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p1));

            public ValueTask<(IEnumerable<AnyClass?> R1, IEnumerable<AnyClass?> R2)> OpClassSeqAsync(
                AnyClass?[] p1,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p1));
            public ValueTask<(MyClassE R1, MyClassE R2)> OpEAsync(
                MyClassE p1,
                int p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p1));

            public ValueTask<(MyClassM R1, MyClassM R2)> OpMAsync(
                MyClassM p1,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p1));

            public ValueTask OpRecursiveAsync(MyClassRecursive? p1, Dispatch dispatch, CancellationToken cancel) =>
                default;

            public ValueTask ThrowMyDerivedExceptionAsync(Dispatch dispatch, CancellationToken cancel) =>
                throw new MyDerivedException(new MyClassA1("a1"),
                                             new MyClassA1("a2"),
                                             new MyClassA1("a3"),
                                             new MyClassA1("a4"));
        }
    }
}
