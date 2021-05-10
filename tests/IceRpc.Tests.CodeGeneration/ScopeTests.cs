using NUnit.Framework;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.CodeGeneration
{
    [Timeout(30000)]
    [Parallelizable(ParallelScope.All)]
    public class ScopeTests
    {
        private readonly Communicator _communicator;
        private readonly Server _server;
        private readonly Scope.IOperationsPrx _prx1;
        private readonly Scope.Inner.IOperationsPrx _prx2;
        private readonly Scope.Inner.Inner2.IOperationsPrx _prx3;
        private readonly Scope.Inner.Test.Inner2.IOperationsPrx _prx4;

        public ScopeTests()
        {
            _communicator = new Communicator();

            var router = new Router();
            router.Map("/test1", new Scope.Operations());
            router.Map("/test2", new Scope.Inner.Operations());
            router.Map("/test3", new Scope.Inner.Inner2.Operations());
            router.Map("/test4", new Scope.Inner.Test.Inner2.Operations());

            _server = new Server()
            {
                Invoker = _communicator,
                Dispatcher = router,
                Endpoint = TestHelper.GetUniqueColocEndpoint()
            };

            _prx1 = Scope.IOperationsPrx.FromServer(_server, "/test1");
            _prx2 = Scope.Inner.IOperationsPrx.FromServer(_server, "/test2");
            _prx3 = Scope.Inner.Inner2.IOperationsPrx.FromServer(_server, "/test3");
            _prx4 = Scope.Inner.Test.Inner2.IOperationsPrx.FromServer(_server, "/test4");

            _server.Listen();
        }

        [OneTimeTearDown]
        public async Task TearDownAsync()
        {
            await _server.DisposeAsync();
            await _communicator.DisposeAsync();
        }

        [Test]
        public async Task Scope_Operations()
        {
            {
                var s = new Scope.S(0);
                Assert.AreEqual(s, await _prx1.OpSAsync(new Scope.S(0)));

                var sseq = new Scope.S[] { s };
                CollectionAssert.AreEqual(sseq, await _prx1.OpSSeqAsync(sseq));

                var smap = new Dictionary<string, Scope.S>() { { "a", s } };
                CollectionAssert.AreEqual(smap, await _prx1.OpSMapAsync(smap));

                var c = new Scope.C(s);
                Assert.AreEqual(s, (await _prx1.OpCAsync(c)).S);

                var cseq1 = new Scope.C[] { c };
                var cseq2 = await _prx1.OpCSeqAsync(cseq1);

                Assert.AreEqual(1, cseq2.Length);
                Assert.AreEqual(s, cseq2[0].S);

                var cmap1 = new Dictionary<string, Scope.C>() { { "a", c } };
                var cmap2 = await _prx1.OpCMapAsync(cmap1);

                Assert.AreEqual(1, cmap2.Count);
                Assert.AreEqual(s, cmap2["a"].S);

                Assert.AreEqual(Scope.E1.v1, await _prx1.OpE1Async(Scope.E1.v1));
                Assert.AreEqual(new Scope.S1("S1"), await _prx1.OpS1Async(new Scope.S1("S1")));
                Assert.AreEqual("C1", (await _prx1.OpC1Async(new Scope.C1("C1"))).S);
            }

            {
                var s = new Scope.Inner.Inner2.S(0);
                Assert.AreEqual(s, await _prx2.OpSAsync(s));

                var sseq = new Scope.Inner.Inner2.S[] { s };
                CollectionAssert.AreEqual(sseq, await _prx2.OpSSeqAsync(sseq));

                var smap = new Dictionary<string, Scope.Inner.Inner2.S>() { { "a", s } };
                CollectionAssert.AreEqual(smap, await _prx2.OpSMapAsync(smap));

                var c = new Scope.Inner.Inner2.C(s);
                Assert.AreEqual(s, (await _prx2.OpCAsync(c)).S);

                var cseq1 = new Scope.Inner.Inner2.C[] { c };
                var cseq2 = await _prx2.OpCSeqAsync(cseq1);

                Assert.AreEqual(1, cseq2.Length);
                Assert.AreEqual(s, cseq2[0].S);

                var cmap1 = new Dictionary<string, Scope.Inner.Inner2.C>() { { "a", c } };
                var cmap2 = await _prx2.OpCMapAsync(cmap1);

                Assert.AreEqual(1, cmap2.Count);
                Assert.AreEqual(s, cmap2["a"].S);
            }

            {
                var s = new Scope.Inner.Inner2.S(0);
                Assert.AreEqual(s, await _prx3.OpSAsync(s));

                var sseq = new Scope.Inner.Inner2.S[] { s };
                CollectionAssert.AreEqual(sseq, await _prx3.OpSSeqAsync(sseq));

                var smap = new Dictionary<string, Scope.Inner.Inner2.S>() { { "a", s } };
                CollectionAssert.AreEqual(smap, await _prx3.OpSMapAsync(smap));

                var c = new Scope.Inner.Inner2.C(s);
                Assert.AreEqual(s, (await _prx3.OpCAsync(c)).S);

                var cseq1 = new Scope.Inner.Inner2.C[] { c };
                var cseq2 = await _prx3.OpCSeqAsync(cseq1);

                Assert.AreEqual(1, cseq2.Length);
                Assert.AreEqual(s, cseq2[0].S);

                var cmap1 = new Dictionary<string, Scope.Inner.Inner2.C>() { { "a", c } };
                var cmap2 = await _prx3.OpCMapAsync(cmap1);

                Assert.AreEqual(1, cmap2.Count);
                Assert.AreEqual(s, cmap2["a"].S);
            }

            {
                var s = new Scope.S(0);
                Assert.AreEqual(s, await _prx4.OpSAsync(s));

                var sseq = new Scope.S[] { s };
                CollectionAssert.AreEqual(sseq, await _prx4.OpSSeqAsync(sseq));

                var smap = new Dictionary<string, Scope.S>() { { "a", s } };
                CollectionAssert.AreEqual(smap, await _prx4.OpSMapAsync(smap));

                var c = new Scope.C(s);
                Assert.AreEqual(s, (await _prx4.OpCAsync(c)).S);

                var cseq1 = new Scope.C[] { c };
                var cseq2 = await _prx4.OpCSeqAsync(cseq1);

                Assert.AreEqual(1, cseq2.Length);
                Assert.AreEqual(s, cseq2[0].S);

                var cmap1 = new Dictionary<string, Scope.C>() { { "a", c } };
                var cmap2 = await _prx4.OpCMapAsync(cmap1);

                Assert.AreEqual(1, cmap2.Count);
                Assert.AreEqual(s, cmap2["a"].S);
            }
        }
    }

    namespace Scope
    {
        public class Operations : IOperations
        {
            public ValueTask<C1> OpC1Async(C1 c1, Dispatch dispatch, CancellationToken cancel) => new(c1);

            public ValueTask<C> OpCAsync(C p1, Dispatch dispatch, CancellationToken cancel) => new(p1);

            public ValueTask<IEnumerable<KeyValuePair<string, C>>> OpCMapAsync(
                Dictionary<string, C> p1,
                Dispatch dispatch, CancellationToken cancel) => new(p1);

            public ValueTask<IEnumerable<C>> OpCSeqAsync(
                C[] p1,
                Dispatch dispatch,
                CancellationToken cancel) => new(p1);

            public ValueTask<E1> OpE1Async(E1 e1, Dispatch dispatch, CancellationToken cancel) => new(e1);

            public ValueTask<S1> OpS1Async(S1 s1, Dispatch dispatch, CancellationToken cancel) => new(s1);

            public ValueTask<S> OpSAsync(S p1, Dispatch dispatch, CancellationToken cancel) => new(p1);

            public ValueTask<IEnumerable<KeyValuePair<string, S>>> OpSMapAsync(
                Dictionary<string, S> p1,
                Dispatch dispatch,
                CancellationToken cancel) => new(p1);

            public ValueTask<IEnumerable<S>> OpSSeqAsync(
                S[] p1,
                Dispatch dispatch,
                CancellationToken cancel) => new(p1);
        }
    }

    namespace Scope.Inner
    {
        public class Operations : IOperations
        {
            public ValueTask<Inner2.C> OpCAsync(Inner2.C p1, Dispatch dispatch, CancellationToken cancel) => new(p1);

            public ValueTask<IEnumerable<KeyValuePair<string, Inner2.C>>> OpCMapAsync(
                Dictionary<string, Inner2.C> p1,
                Dispatch dispatch,
                CancellationToken cancel) => new(p1);

            public ValueTask<IEnumerable<Inner2.C>> OpCSeqAsync(
                Inner2.C[] p1,
                Dispatch dispatch,
                CancellationToken cancel) => new(p1);

            public ValueTask<Inner2.S> OpSAsync(Inner2.S p1, Dispatch dispatch, CancellationToken cancel) => new(p1);

            public ValueTask<IEnumerable<KeyValuePair<string, Inner2.S>>> OpSMapAsync(
                Dictionary<string, Inner2.S> p1,
                Dispatch dispatch,
                CancellationToken cancel) => new(p1);

            public ValueTask<IEnumerable<Inner2.S>> OpSSeqAsync(
                Inner2.S[] p1,
                Dispatch dispatch,
                CancellationToken cancel) => new(p1);
        }
    }

    namespace Scope.Inner.Inner2
    {
        public class Operations : IOperations
        {
            public ValueTask<C> OpCAsync(C c1, Dispatch dispatch, CancellationToken cancel) => new(c1);

            public ValueTask<IEnumerable<KeyValuePair<string, C>>> OpCMapAsync(
                Dictionary<string, C> p1,
                Dispatch dispatch,
                CancellationToken cancel) => new(p1);

            public ValueTask<IEnumerable<C>> OpCSeqAsync(
                C[] p1,
                Dispatch dispatch,
                CancellationToken cancel) => new(p1);

            public ValueTask<S> OpSAsync(S p1, Dispatch dispatch, CancellationToken cancel) => new(p1);

            public ValueTask<IEnumerable<KeyValuePair<string, S>>> OpSMapAsync(
                Dictionary<string, S> p1,
                Dispatch dispatch,
                CancellationToken cancel) => new(p1);

            public ValueTask<IEnumerable<S>> OpSSeqAsync(
                S[] p1,
                Dispatch dispatch,
                CancellationToken cancel) => new(p1);
        }
    }

    namespace Scope.Inner.Test.Inner2
    {
        public class Operations : IOperations
        {
            public ValueTask<Scope.C> OpCAsync(
                Scope.C p1,
                Dispatch dispatch,
                CancellationToken cancel) => new(p1);

            public ValueTask<IEnumerable<KeyValuePair<string, Scope.C>>> OpCMapAsync(
                Dictionary<string, Scope.C> p1,
                Dispatch dispatch,
                CancellationToken cancel) => new(p1);

            public ValueTask<IEnumerable<Scope.C>> OpCSeqAsync(
                Scope.C[] c1,
                Dispatch dispatch,
                CancellationToken cancel) => new(c1);

            public ValueTask<Scope.S> OpSAsync(
                Scope.S p1,
                Dispatch dispatch,
                CancellationToken cancel) => new(p1);

            public ValueTask<IEnumerable<KeyValuePair<string, Scope.S>>> OpSMapAsync(
                Dictionary<string, Scope.S> p1,
                Dispatch dispatch,
                CancellationToken cancel) => new(p1);

            public ValueTask<IEnumerable<Scope.S>> OpSSeqAsync(
                Scope.S[] p1,
                Dispatch dispatch,
                CancellationToken cancel) => new(p1);
        }
    }
}
