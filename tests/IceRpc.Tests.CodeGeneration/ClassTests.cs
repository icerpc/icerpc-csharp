// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.CodeGeneration
{
    public partial class MyBaseClass1
    {
        // Overwrite property to ensure this value takes preference over a value set by
        // the constructor.
        partial void Initialize() => Id = "My id";
    }

    public partial class MyDerivedClass1
    {
        // Overwrite property to ensure this value takes preference over a value set by
        // the constructor.
        partial void Initialize() => Name = "My name";
    }

    public partial class MyClass2
    {
        public bool Called { get; set; }

        // Ensure that partial initialize is also called for classes without data members
        partial void Initialize() => Called = true;
    }

    [Timeout(30000)]
    [Parallelizable(ParallelScope.All)]
    [TestFixture(Protocol.Ice1)]
    [TestFixture(Protocol.Ice2)]
    public class ClassTests
    {
        private readonly Communicator _communicator;
        private readonly Server _server;
        private readonly IClassOperationsPrx _prx;
        private readonly IClassOperationsUnexpectedClassPrx _prxUnexpectedClass;

        public ClassTests(Protocol protocol)
        {
            _communicator = new Communicator();
            _server = new Server(_communicator,
                new ServerOptions()
                {
                    Protocol = protocol,
                    ColocationScope = ColocationScope.Communicator
                });
            _prx = _server.Add("/test", new ClassOperations(), IClassOperationsPrx.Factory);
            _prxUnexpectedClass = _server.Add("/test1",
                                               new ClassOperationsUnexpectedClass(),
                                               IClassOperationsUnexpectedClassPrx.Factory);
        }

        [OneTimeTearDown]
        public async Task TearDownAsync()
        {
            await _server.DisposeAsync();
            await _communicator.DisposeAsync();
        }

        [Test]
        public async Task Class_OperationsAsync()
        {
            MyClassB? b1 = await _prx.GetB1Async();
            Assert.IsNotNull(b1);

            MyClassB? b2 = await _prx.GetB2Async();
            Assert.IsNotNull(b2);

            MyClassC? c = await _prx.GetCAsync();
            Assert.IsNotNull(c != null);

            MyClassD? d = await _prx.GetDAsync();
            Assert.IsNotNull(d);

            Assert.AreNotEqual(b1, b2);
            Assert.AreEqual(b1.TheB, b1);
            Assert.IsNull(b1.TheC);
            Assert.IsInstanceOf<MyClassB>(b1.TheA);
            Assert.AreEqual(((MyClassB)b1.TheA!).TheA, b1.TheA);
            Assert.AreEqual(((MyClassB)b1.TheA!).TheB, b1);
            Assert.AreEqual(((MyClassB)b1.TheA).TheC!.TheB, b1.TheA);

            Assert.AreEqual(b2.TheA, b2);
            Assert.IsNull(d.TheC);

            (MyClassB? r1, MyClassB? r2, MyClassC? r3, MyClassD? r4) = await _prx.GetAllAsync();
            Assert.IsNotNull(r1);
            Assert.IsNotNull(r2);
            Assert.IsNotNull(r3);
            Assert.IsNotNull(r4);

            Assert.AreNotEqual(r1, r2);
            Assert.AreEqual(r1.TheA, r2);
            Assert.AreEqual(r1.TheB, r1);
            Assert.IsNull(r1.TheC);
            Assert.AreEqual(r2.TheA, r2);
            Assert.AreEqual(r2.TheB, r1);
            Assert.AreEqual(r2.TheC, r3);
            Assert.AreEqual(r3.TheB, r2);
            Assert.AreEqual(r4.TheA, r1);
            Assert.AreEqual(r4.TheB, r2);
            Assert.IsNull(r4.TheC);

            MyClassK? k = await _prx.GetKAsync();
            var l = k!.Value as MyClassL;
            Assert.IsNotNull(l);
            Assert.AreEqual("l", l.Data);

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

            MyDerivedException? ex = Assert.ThrowsAsync<MyDerivedException>(
                async () => await _prx.ThrowMyDerivedExceptionAsync());
            Assert.AreEqual("a1", ex!.A1!.Name);
            Assert.AreEqual("a2", ex!.A2!.Name);
            Assert.AreEqual("a3", ex!.A3!.Name);
            Assert.AreEqual("a4", ex!.A4!.Name);

            (MyClassE e1, MyClassE e2) = await _prx.OpEAsync(new MyClassE(theB: new MyClassB(), theC: new MyClassC()));
            Assert.IsNotNull(e1);
            Assert.IsInstanceOf<MyClassB>(e1.TheB);
            Assert.IsInstanceOf<MyClassC>(e1.TheC);

            Assert.IsNotNull(e2);
            Assert.IsInstanceOf<MyClassB>(e2.TheB);
            Assert.IsInstanceOf<MyClassC>(e2.TheC);
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

            Assert.IsNotNull(m1);
            Assert.AreEqual(2, m1.V.Count);
            Assert.AreEqual("one", m1.V[k1].Data);
            Assert.AreEqual("two", m1.V[k2].Data);

            Assert.IsNotNull(m2);
            Assert.AreEqual(2, m2.V.Count);
            Assert.AreEqual("one", m2.V[k1].Data);
            Assert.AreEqual("two", m2.V[k2].Data);
        }

        [Test]
        public async Task Class_PartialInitializeAsync()
        {
            Assert.AreEqual("My id", new MyBaseClass1("").Id);

            var derived1 = new MyDerivedClass1("", "");
            Assert.AreEqual("My id", derived1.Id);
            Assert.AreEqual("My name", derived1.Name);

            Assert.AreEqual("My id", new MyDerivedClass2("").Id);

            Assert.IsTrue(new MyClass2().Called);

            // Ensure the partial Initialize method called when unmarshaling a class
            derived1 = await _prx.GetMyDerivedClass1Async();
            Assert.AreEqual("My id", derived1.Id);
            Assert.AreEqual("My name", derived1.Name);

            MyDerivedClass2 derived2 = await _prx.GetMyDerivedClass2Async();
            Assert.AreEqual("My id", derived2.Id);

            MyClass2 class2 = await _prx.GetMyClass2Async();
            Assert.IsTrue(class2.Called);
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
            Assert.IsNotNull(await _prx.GetCompactAsync());

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

        public class ClassOperations : IAsyncClassOperations
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
                Current current,
                CancellationToken cancel) => new((_b1, _b2, _c, _d));

            public ValueTask<MyClassB> GetB1Async(Current current, CancellationToken cancel) => new(_b1);

            public ValueTask<MyClassB> GetB2Async(Current current, CancellationToken cancel) => new(_b2);

            public ValueTask<MyClassC> GetCAsync(Current current, CancellationToken cancel) => new(_c);

            public ValueTask<MyCompactClass> GetCompactAsync(Current current, CancellationToken cancel) =>
                new(new MyDerivedCompactClass());

            public ValueTask<MyClassD1> GetD1Async(MyClassD1 p1, Current current, CancellationToken cancel) =>
                new(p1);

            public ValueTask<MyClassD> GetDAsync(Current current, CancellationToken cancel) => new(_d);

            public ValueTask<MyClassK> GetKAsync(Current current, CancellationToken cancel) =>
                new(new MyClassK(new MyClassL("l")));
            public ValueTask<MyClass2> GetMyClass2Async(Current current, CancellationToken cancel) =>
                new (new MyClass2());
            public ValueTask<MyDerivedClass1> GetMyDerivedClass1Async(Current current, CancellationToken cancel)
            {
                // We don't pass the values in the constructor because the partial initialize implementation overrides them
                // for testing purpose.
                var derived = new MyDerivedClass1("", "");
                derived.Id = "Other id";
                derived.Name = "Other name";
                return new(derived);
            }
            public ValueTask<MyDerivedClass2> GetMyDerivedClass2Async(Current current, CancellationToken cancel)
            {
                // We don't pass the values in the constructor because the partial initialize implementation overrides them
                // for testing purpose.
                var derived = new MyDerivedClass2("");
                derived.Id = "Other id";
                return new(derived);
            }
            public ValueTask<(AnyClass? R1, AnyClass? R2)> OpClassAsync(
                AnyClass? p1,
                Current current,
                CancellationToken cancel) => new((p1, p1));

            public ValueTask<(IReadOnlyDictionary<string, AnyClass?> R1, IReadOnlyDictionary<string, AnyClass?> R2)> OpClassMapAsync(
                Dictionary<string, AnyClass?> p1,
                Current current,
                CancellationToken cancel) => new((p1, p1));

            public ValueTask<(IEnumerable<AnyClass?> R1, IEnumerable<AnyClass?> R2)> OpClassSeqAsync(
                AnyClass?[] p1,
                Current current,
                CancellationToken cancel) => new((p1, p1));
            public ValueTask<(MyClassE R1, MyClassE R2)> OpEAsync(
                MyClassE p1,
                Current current,
                CancellationToken cancel) => new((p1, p1));

            public ValueTask<(MyClassM R1, MyClassM R2)> OpMAsync(
                MyClassM p1,
                Current current,
                CancellationToken cancel) => new ((p1, p1));

            public ValueTask OpRecursiveAsync(MyClassRecursive? p1, Current current, CancellationToken cancel) =>
                default;

            public ValueTask ThrowMyDerivedExceptionAsync(Current current, CancellationToken cancel) =>
                throw new MyDerivedException(new MyClassA1("a1"),
                                             new MyClassA1("a2"),
                                             new MyClassA1("a3"),
                                             new MyClassA1("a4"));
        }

        public class ClassOperationsUnexpectedClass : IService
        {
            public ValueTask<OutgoingResponseFrame> DispatchAsync(Current current, CancellationToken cancel) =>
                new (OutgoingResponseFrame.WithReturnValue(current,
                                                           compress: false,
                                                           format: default,
                                                           new MyClassAlsoEmpty(),
                                                           (ostr, ae) => ostr.WriteClass(ae, null)));
        }
    }
}
