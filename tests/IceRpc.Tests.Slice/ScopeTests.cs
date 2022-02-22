// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Tests.Slice
{
    [Timeout(30000)]
    [Parallelizable(ParallelScope.All)]
    public sealed class ScopeTests
    {
        private readonly ServiceProvider _serviceProvider;
        private readonly Scope.OperationsPrx _prx1;
        private readonly Scope.Inner.OperationsPrx _prx2;
        private readonly Scope.Inner.Inner2.OperationsPrx _prx3;
        private readonly Scope.Inner.Test.Inner2.OperationsPrx _prx4;

        public ScopeTests()
        {
            _serviceProvider = new IntegrationTestServiceCollection()
                .AddTransient<IDispatcher>(_ =>
                {
                    var router = new Router();
                    router.Map<Scope.IOperations>(new Scope.Operations());
                    router.Map<Scope.Inner.IOperations>(new Scope.Inner.Operations());
                    router.Map<Scope.Inner.Inner2.IOperations>(new Scope.Inner.Inner2.Operations());
                    router.Map<Scope.Inner.Test.Inner2.IOperations>(new Scope.Inner.Test.Inner2.Operations());
                    return router;
                })
                .BuildServiceProvider();

            Connection connection = _serviceProvider.GetRequiredService<Connection>();
            _prx1 = Scope.OperationsPrx.FromConnection(connection);
            _prx2 = Scope.Inner.OperationsPrx.FromConnection(connection);
            _prx3 = Scope.Inner.Inner2.OperationsPrx.FromConnection(connection);
            _prx4 = Scope.Inner.Test.Inner2.OperationsPrx.FromConnection(connection);
        }

        [OneTimeTearDown]
        public ValueTask DisposeAsync() => _serviceProvider.DisposeAsync();

        [Test]
        public async Task Scope_Operations()
        {
            {
                var s = new Scope.S(0);
               Assert.That(await _prx1.OpSAsync(new Scope.S(0)), Is.EqualTo(s));

                var sseq = new Scope.S[] { s };
               Assert.That(await _prx1.OpSSeqAsync(sseq), Is.EqualTo(sseq));

                var smap = new Dictionary<string, Scope.S>() { { "a", s } };
               Assert.That(await _prx1.OpSMapAsync(smap), Is.EqualTo(smap));

                var c = new Scope.C(s);
               Assert.That((await _prx1.OpCAsync(c)).S, Is.EqualTo(s));

                var cseq1 = new Scope.C[] { c };
                var cseq2 = await _prx1.OpCSeqAsync(cseq1);

               Assert.That(cseq2.Length, Is.EqualTo(1));
               Assert.That(cseq2[0].S, Is.EqualTo(s));

                var cmap1 = new Dictionary<string, Scope.C>() { { "a", c } };
                var cmap2 = await _prx1.OpCMapAsync(cmap1);

               Assert.That(cmap2.Count, Is.EqualTo(1));
               Assert.That(cmap2["a"].S, Is.EqualTo(s));

               Assert.That(await _prx1.OpE1Async(Scope.E1.v1), Is.EqualTo(Scope.E1.v1));
               Assert.That(await _prx1.OpS1Async(new Scope.S1("S1")), Is.EqualTo(new Scope.S1("S1")));
               Assert.That((await _prx1.OpC1Async(new Scope.C1("C1"))).S, Is.EqualTo("C1"));
            }

            {
                var s = new Scope.Inner.Inner2.S(0);
               Assert.That(await _prx2.OpSAsync(s), Is.EqualTo(s));

                var sseq = new Scope.Inner.Inner2.S[] { s };
               Assert.That(await _prx2.OpSSeqAsync(sseq), Is.EqualTo(sseq));

                var smap = new Dictionary<string, Scope.Inner.Inner2.S>() { { "a", s } };
               Assert.That(await _prx2.OpSMapAsync(smap), Is.EqualTo(smap));

                var c = new Scope.Inner.Inner2.C(s);
               Assert.That((await _prx2.OpCAsync(c)).S, Is.EqualTo(s));

                var cseq1 = new Scope.Inner.Inner2.C[] { c };
                var cseq2 = await _prx2.OpCSeqAsync(cseq1);

               Assert.That(cseq2.Length, Is.EqualTo(1));
               Assert.That(cseq2[0].S, Is.EqualTo(s));

                var cmap1 = new Dictionary<string, Scope.Inner.Inner2.C>() { { "a", c } };
                var cmap2 = await _prx2.OpCMapAsync(cmap1);

               Assert.That(cmap2.Count, Is.EqualTo(1));
               Assert.That(cmap2["a"].S, Is.EqualTo(s));
            }

            {
                var s = new Scope.Inner.Inner2.S(0);
               Assert.That(await _prx3.OpSAsync(s), Is.EqualTo(s));

                var sseq = new Scope.Inner.Inner2.S[] { s };
               Assert.That(await _prx3.OpSSeqAsync(sseq), Is.EqualTo(sseq));

                var smap = new Dictionary<string, Scope.Inner.Inner2.S>() { { "a", s } };
               Assert.That(await _prx3.OpSMapAsync(smap), Is.EqualTo(smap));

                var c = new Scope.Inner.Inner2.C(s);
               Assert.That((await _prx3.OpCAsync(c)).S, Is.EqualTo(s));

                var cseq1 = new Scope.Inner.Inner2.C[] { c };
                var cseq2 = await _prx3.OpCSeqAsync(cseq1);

               Assert.That(cseq2.Length, Is.EqualTo(1));
               Assert.That(cseq2[0].S, Is.EqualTo(s));

                var cmap1 = new Dictionary<string, Scope.Inner.Inner2.C>() { { "a", c } };
                var cmap2 = await _prx3.OpCMapAsync(cmap1);

               Assert.That(cmap2.Count, Is.EqualTo(1));
               Assert.That(cmap2["a"].S, Is.EqualTo(s));
            }

            {
                var s = new Scope.S(0);
               Assert.That(await _prx4.OpSAsync(s), Is.EqualTo(s));

                var sseq = new Scope.S[] { s };
               Assert.That(await _prx4.OpSSeqAsync(sseq), Is.EqualTo(sseq));

                var smap = new Dictionary<string, Scope.S>() { { "a", s } };
               Assert.That(await _prx4.OpSMapAsync(smap), Is.EqualTo(smap));

                var c = new Scope.C(s);
               Assert.That((await _prx4.OpCAsync(c)).S, Is.EqualTo(s));

                var cseq1 = new Scope.C[] { c };
                var cseq2 = await _prx4.OpCSeqAsync(cseq1);

               Assert.That(cseq2.Length, Is.EqualTo(1));
               Assert.That(cseq2[0].S, Is.EqualTo(s));

                var cmap1 = new Dictionary<string, Scope.C>() { { "a", c } };
                var cmap2 = await _prx4.OpCMapAsync(cmap1);

               Assert.That(cmap2.Count, Is.EqualTo(1));
               Assert.That(cmap2["a"].S, Is.EqualTo(s));
            }
        }
    }

    namespace Scope
    {
        public class Operations : Service, IOperations
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
        public class Operations : Service, IOperations
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
        public class Operations : Service, IOperations
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
        public class Operations : Service, IOperations
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
