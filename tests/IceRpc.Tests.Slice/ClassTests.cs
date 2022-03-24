// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Tests.Slice
{
    [Timeout(30000)]
    [Parallelizable(ParallelScope.All)]
    [TestFixture("ice")]
    [TestFixture("icerpc")]
    public sealed class ClassTests
    {
        private readonly ServiceProvider _serviceProvider;
        private readonly ClassOperationsPrx _prx;
        private readonly ClassOperationsUnexpectedClassPrx _prxUnexpectedClass;

        public ClassTests(string protocol)
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
                                return new(new OutgoingResponse(request)
                                {
                                    PayloadSource = Encode()
                                });

                                static System.IO.Pipelines.PipeReader Encode()
                                {
                                    var pipe = new System.IO.Pipelines.Pipe(); // TODO: pipe options

                                    var encoder = new SliceEncoder(pipe.Writer, Encoding.Slice11, default);
                                    encoder.EncodeClass(new MyClassAlsoEmpty());
                                    pipe.Writer.Complete();  // flush to reader and sets Is[Writer]Completed to true.
                                    return pipe.Reader;
                                }
                            }));
                        return router;
                    })
                .BuildServiceProvider();

            Connection connection = _serviceProvider.GetRequiredService<Connection>();
            _prx = ClassOperationsPrx.FromConnection(connection);
            _prxUnexpectedClass = ClassOperationsUnexpectedClassPrx.FromConnection(connection);
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
            Assert.That(b1, Is.EqualTo(b1.TheB));
            Assert.That(b1.TheC, Is.Null);
            Assert.That(b1.TheA, Is.InstanceOf<MyClassB>());
            Assert.That(b1.TheA, Is.EqualTo(((MyClassB)b1.TheA!).TheA));
            Assert.That(b1, Is.EqualTo(((MyClassB)b1.TheA!).TheB));
            Assert.That(b1.TheA, Is.EqualTo(((MyClassB)b1.TheA).TheC!.TheB));

            Assert.That(b2, Is.EqualTo(b2.TheA));
            Assert.That(d.TheC, Is.Null);

            (MyClassB? r1, MyClassB? r2, MyClassC? r3, MyClassD? r4) = await _prx.GetAllAsync();
            Assert.That(r1, Is.Not.Null);
            Assert.That(r2, Is.Not.Null);
            Assert.That(r3, Is.Not.Null);
            Assert.That(r4, Is.Not.Null);

            Assert.AreNotEqual(r1, r2);
            Assert.That(r2, Is.EqualTo(r1.TheA));
            Assert.That(r1, Is.EqualTo(r1.TheB));
            Assert.That(r1.TheC, Is.Null);
            Assert.That(r2, Is.EqualTo(r2.TheA));
            Assert.That(r1, Is.EqualTo(r2.TheB));
            Assert.That(r3, Is.EqualTo(r2.TheC));
            Assert.That(r2, Is.EqualTo(r3.TheB));
            Assert.That(r1, Is.EqualTo(r4.TheA));
            Assert.That(r2, Is.EqualTo(r4.TheB));
            Assert.That(r4.TheC, Is.Null);

            MyClassK? k = await _prx.GetKAsync();
            var l = k!.Value as MyClassL;
            Assert.That(l, Is.Not.Null);
            Assert.That(l!.Data, Is.EqualTo("l"));

            MyClassD1? d1 = await _prx.GetD1Async(
                new MyClassD1(
                    new MyClassA1("a1"),
                    new MyClassA1("a2"),
                    new MyClassA1("a3"),
                    new MyClassA1("a4")));
            Assert.That(d1!.A1!.Name, Is.EqualTo("a1"));
            Assert.That(d1!.A2!.Name, Is.EqualTo("a2"));
            Assert.That(d1!.A3!.Name, Is.EqualTo("a3"));
            Assert.That(d1!.A4!.Name, Is.EqualTo("a4"));

            MyDerivedException? ex = Assert.ThrowsAsync<MyDerivedException>(
                async () => await _prx.ThrowMyDerivedExceptionAsync());
            Assert.That(ex!.A1!.Name, Is.EqualTo("a1"));
            Assert.That(ex!.A2!.Name, Is.EqualTo("a2"));
            Assert.That(ex!.A3!.Name, Is.EqualTo("a3"));
            Assert.That(ex!.A4!.Name, Is.EqualTo("a4"));

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
            var d = new Dictionary<MyCompactStruct, MyClassL>();

            var k1 = new MyCompactStruct(1, 1);
            d[k1] = new MyClassL("one");

            var k2 = new MyCompactStruct(2, 2);
            d[k2] = new MyClassL("two");

            (MyClassM m2, MyClassM m1) = await _prx.OpMAsync(new MyClassM(d));

            Assert.That(m1, Is.Not.Null);
            Assert.That(m1.V.Count, Is.EqualTo(2));
            Assert.That(m1.V[k1].Data, Is.EqualTo("one"));
            Assert.That(m1.V[k2].Data, Is.EqualTo("two"));

            Assert.That(m2, Is.Not.Null);
            Assert.That(m2.V.Count, Is.EqualTo(2));
            Assert.That(m2.V[k1].Data, Is.EqualTo("one"));
            Assert.That(m2.V[k2].Data, Is.EqualTo("two"));
        }

        [Test]
        public async Task Class_AnyClassParameterAsync()
        {
            // testing AnyClass as parameter
            {
                (AnyClass? v3, AnyClass? v2) = await _prx.OpClassAsync(new MyClassL("l"));
                Assert.That(((MyClassL)v2!).Data, Is.EqualTo("l"));
                Assert.That(((MyClassL)v3!).Data, Is.EqualTo("l"));
            }

            {
                (AnyClass?[] v3, AnyClass?[] v2) = await _prx.OpClassSeqAsync(new AnyClass[] { new MyClassL("l") });
                Assert.That(((MyClassL)v2[0]!).Data, Is.EqualTo("l"));
                Assert.That(((MyClassL)v3[0]!).Data, Is.EqualTo("l"));
            }

            {
                var v1 = new Dictionary<string, AnyClass?> { { "l", new MyClassL("l") } };
                (Dictionary<string, AnyClass?> v3, Dictionary<string, AnyClass?> v2) = await _prx.OpClassMapAsync(v1);
                Assert.That(((MyClassL)v2["l"]!).Data, Is.EqualTo("l"));
                Assert.That(((MyClassL)v3["l"]!).Data, Is.EqualTo("l"));
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
            catch (DispatchException ex) when (ex.ErrorCode == DispatchErrorCode.InvalidData)
            {
                // Expected decode exception from the server (maximum depth reached)
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
