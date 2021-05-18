// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.Api
{
    // Each test case gets a fresh communicator, server and router.
    [Parallelizable(scope: ParallelScope.All)]
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    public class RouterTests
    {
        private static IDispatcher _failDispatcher = new InlineDispatcher(
                async (current, cancel) =>
                {
                    Assert.Fail();
                    return await _service!.DispatchAsync(current, cancel);
                });

        private static IDispatcher _service = new Greeter();

        private Connection _connection;

        private Router _router = new Router();
        private Server _server;

        public RouterTests()
        {
            _server = new Server
            {
                Dispatcher = _router,
                Endpoint = TestHelper.GetUniqueColocEndpoint()
            };
            _server.Listen();

            _connection = new Connection { RemoteEndpoint = _server.ProxyEndpoint };
        }

        [Test]
        public void Router_BadPath()
        {
            _router.Mount("/", _failDispatcher);
            string badPath = "/a/b/c/d/e/f/g/h/i/j/k/l/m/n/o/p/q";
            var greeter = IGreeterPrx.FromConnection(_connection, badPath);
            Assert.ThrowsAsync<DispatchException>(async () => await greeter.IcePingAsync());

            Assert.Throws<ArgumentException>(() => _router.Map("foo", _failDispatcher));
            Assert.Throws<ArgumentException>(() => _router.Mount("foo", _failDispatcher));
        }

        [TestCase("/foo")]
        [TestCase("/foo/a/b/c/d")]
        [TestCase("/")]
        [TestCase("///")] // bad form but nevertheless still works
        [TestCase("/foo/////")] // bad form but nevertheless still works
        public async Task Router_MapMountAsync(string path)
        {
            int value = 0;

            // Verifies that exact match is selected first.
            _router.Map(path, new InlineDispatcher(
                async (current, cancel) =>
                {
                    value = 1;
                    Assert.AreEqual(path, current.Path);
                    return await _service.DispatchAsync(current, cancel);
                }));

            _router.Mount(path, _failDispatcher);

            IGreeterPrx greeter = IGreeterPrx.FromConnection(_connection, path);
            await greeter.IcePingAsync();
            Assert.AreEqual(1, value);

            // Without exact match from Map, we hit the mounted route:
            Assert.IsTrue(_router.Unmap(path));

            _router.Mount(path, new InlineDispatcher(
                async (current, cancel) =>
                {
                    value = 2;
                    Assert.AreEqual(path, current.Path);
                    return await _service.DispatchAsync(current, cancel);
                }));

            await greeter.IcePingAsync();
            Assert.AreEqual(2, value);
            value = 0;

            // With a slightly different Map-path, we still hit the mounted route

            _router.Map($"{path}/", _failDispatcher);
            await greeter.IcePingAsync();
            Assert.AreEqual(2, value);
        }

        [TestCase("/foo", "/foo/")]
        [TestCase("/foo/", "/foo")]
        [TestCase("//", "/")]
        public void Router_MapNotFound(string registered, string path)
        {
            _router.Map(registered, _failDispatcher);
            var greeter = IGreeterPrx.FromConnection(_connection, path);
            Assert.ThrowsAsync<ServiceNotFoundException>(async () => await greeter.IcePingAsync());
        }

        [TestCase("/foo", "/foo/bar")]
        [TestCase("/foo/", "/foo/bar")]
        [TestCase("/foo/bar///", "/foo/bar")] // ignores trailing slash(es) in prefix
        [TestCase("/foo///bar/a", "/foo///bar/a/b/c/d")]
        public async Task Router_MountAsync(string prefix, string path)
        {
            bool called = false;

            _router.Mount(prefix, new InlineDispatcher(
                async (current, cancel) =>
                {
                    called = true;
                    Assert.AreEqual(path, current.Path);
                    Assert.That(current.Path, Does.StartWith(prefix.TrimEnd('/')));
                    return await _service.DispatchAsync(current, cancel);
                }));

            var greeter = IGreeterPrx.FromConnection(_connection, path);
            await greeter.IcePingAsync();
            Assert.IsTrue(called);
        }

        [TestCase("/foo", "/foobar")]
        [TestCase("/foo/bar", "/foo//bar")]
        public void Router_MountNotFound(string registered, string path)
        {
            _router.Mount(registered, _failDispatcher);
            var greeter = IGreeterPrx.FromConnection(_connection, path);
            Assert.ThrowsAsync<ServiceNotFoundException>(async () => await greeter.IcePingAsync());
        }

        [TestCase("/foo", "/foo/bar", "/bar")]
        [TestCase("/foo/", "/foo/bar", "/bar")]
        [TestCase("/foo/bar///", "/foo/bar/", "/")]
        [TestCase("/foo///bar/a", "/foo///bar/a/b/c/d", "/b/c/d")]
        public async Task Router_RouteAsync(string prefix, string path, string subpath)
        {
            Assert.IsEmpty(_router.AbsolutePrefix);
            bool mainRouterMiddlewareCalled = false;
            bool subRouterMiddlewareCalled = false;

            _router.Use(next => new InlineDispatcher(
                (request, cancel) =>
                {
                    mainRouterMiddlewareCalled = true;
                    Assert.AreEqual(path, request.Path);
                    return next.DispatchAsync(request, cancel);
                }));

            _router.Route(prefix, r =>
                {
                    Assert.AreEqual(prefix.TrimEnd('/'), r.AbsolutePrefix);
                    r.Map(subpath, new InlineDispatcher(
                        (request, cancel) =>
                        {
                            subRouterMiddlewareCalled = true;
                            Assert.AreEqual(path, request.Path);
                            Assert.AreEqual($"{r.AbsolutePrefix}{subpath}", request.Path);
                            return _service.DispatchAsync(request, cancel);
                        }));
                });

            var greeter = IGreeterPrx.FromConnection(_connection, path);
            await greeter.IcePingAsync();
            Assert.IsTrue(mainRouterMiddlewareCalled);
            Assert.IsTrue(subRouterMiddlewareCalled);
        }

        // Same test as above with one more level of nesting
        [TestCase("/foo", "/bar", "/foo/bar/abc", "/abc")]
        [TestCase("/foo/", "/bar/", "/foo/bar/abc", "/abc")]
        public async Task Router_RouteNestedAsync(string prefix, string subprefix, string path, string subpath)
        {
            Assert.IsEmpty(_router.AbsolutePrefix);

            bool mainRouterMiddlewareCalled = false;
            bool nestedRouterMiddlewareCalled = false;

            _router.Use(next => new InlineDispatcher(
                (request, cancel) =>
                {
                    mainRouterMiddlewareCalled = true;
                    Assert.AreEqual(path, request.Path);
                    return next.DispatchAsync(request, cancel);
                }));

            _router.Route(prefix, r =>
                {
                    Assert.AreEqual(prefix.TrimEnd('/'), r.AbsolutePrefix);
                    r.Route(subprefix, r =>
                    {
                        r.Map(subpath, new InlineDispatcher(
                            async (current, cancel) =>
                            {
                                nestedRouterMiddlewareCalled = true;
                                Assert.AreEqual(path, current.Path);
                                Assert.AreEqual($"{r.AbsolutePrefix}{subpath}", current.Path);
                                return await _service.DispatchAsync(current, cancel);
                            }));
                    });
                });

            var greeter = IGreeterPrx.FromConnection(_connection, path);
            await greeter.IcePingAsync();
            Assert.IsTrue(mainRouterMiddlewareCalled);
            Assert.IsTrue(nestedRouterMiddlewareCalled);
        }

        [Test]
        public async Task Router_RouteDefaultPathAsync()
        {
            Assert.IsEmpty(_router.AbsolutePrefix);

            Assert.ThrowsAsync<ServiceNotFoundException>(
                async () => await IGreeterPrx.FromConnection(_connection).IcePingAsync());
            _router.Map<IGreeter>(new Greeter());
            await IGreeterPrx.FromConnection(_connection).IcePingAsync();

            Assert.ThrowsAsync<ServiceNotFoundException>(
                async () => await IBaseAPrx.FromConnection(_connection).IcePingAsync());
            _router.Map<IBaseA>(new BaseA());
            await IBaseAPrx.FromConnection(_connection).IcePingAsync();

            Assert.ThrowsAsync<ServiceNotFoundException>(
                async () => await IDerivedAPrx.FromConnection(_connection).IcePingAsync());
            _router.Map<IDerivedA>(new DerivedA());
            await IDerivedAPrx.FromConnection(_connection).IcePingAsync();

            Assert.ThrowsAsync<ServiceNotFoundException>(
                async () => await IMostDerivedAPrx.FromConnection(_connection).IcePingAsync());
            _router.Map<IMostDerivedA>(new MostDerivedA());
            await IMostDerivedAPrx.FromConnection(_connection).IcePingAsync();

            Assert.ThrowsAsync<ServiceNotFoundException>(
                async () => await IBaseBPrx.FromConnection(_connection).IcePingAsync());
            _router.Map<IBaseB>(new BaseB());
            await IBaseBPrx.FromConnection(_connection).IcePingAsync();

            Assert.ThrowsAsync<ServiceNotFoundException>(
                async () => await IDerivedBPrx.FromConnection(_connection).IcePingAsync());
            _router.Map<IDerivedB>(new DerivedB());
            await IDerivedBPrx.FromConnection(_connection).IcePingAsync();

            Assert.ThrowsAsync<ServiceNotFoundException>(
                async () => await IMostDerivedBPrx.FromConnection(_connection).IcePingAsync());
            _router.Map<IMostDerivedB>(new MostDerivedB());
            await IMostDerivedBPrx.FromConnection(_connection).IcePingAsync();

            Assert.ThrowsAsync<ServiceNotFoundException>(
                async () => await IBaseCPrx.FromConnection(_connection).IcePingAsync());
            _router.Map<IBaseC>(new BaseC());
            await IBaseCPrx.FromConnection(_connection).IcePingAsync();

            Assert.ThrowsAsync<ServiceNotFoundException>(
                async () => await IDerivedCPrx.FromConnection(_connection).IcePingAsync());
            _router.Map<IDerivedC>(new DerivedC());
            await IDerivedCPrx.FromConnection(_connection).IcePingAsync();

            Assert.ThrowsAsync<ServiceNotFoundException>(
                async () => await IMostDerivedCPrx.FromConnection(_connection).IcePingAsync());
            _router.Map<IMostDerivedC>(new MostDerivedC());
            await IMostDerivedCPrx.FromConnection(_connection).IcePingAsync();
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            await _server.ShutdownAsync();
            await _connection.ShutdownAsync();
        }

        public class Greeter : IGreeter
        {
            public ValueTask SayHelloAsync(Dispatch dispatch, CancellationToken cancel) =>
                throw new NotImplementedException();
        }

        public class BaseA : IBaseA { }
        public class DerivedA : IDerivedA { }
        public class MostDerivedA : IMostDerivedA { }

        public class BaseB : IBaseB { }
        public class DerivedB : IDerivedB { }
        public class MostDerivedB : IMostDerivedB { }

        public class BaseC : IBaseC { }
        public class DerivedC : IDerivedC { }
        public class MostDerivedC : IMostDerivedC { }
    }
}
