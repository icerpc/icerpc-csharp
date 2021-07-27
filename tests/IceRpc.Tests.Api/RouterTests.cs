// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.Api
{
    // Each test case gets a fresh communicator, server and router.
    [Timeout(30000)]
    [Parallelizable(scope: ParallelScope.All)]
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    public sealed class RouterTests : IAsyncDisposable
    {
        private static readonly IDispatcher _failDispatcher = new InlineDispatcher(
                async (current, cancel) =>
                {
                    Assert.Fail();
                    return await _service!.DispatchAsync(current, cancel);
                });

        private static readonly IDispatcher _service = new Greeter();

        private readonly Connection _connection;

        private readonly Router _router = new();
        private readonly Server _server;

        public RouterTests()
        {
            _server = new Server
            {
                Dispatcher = _router,
                Endpoint = TestHelper.GetUniqueColocEndpoint()
            };
            _server.Listen();

            _connection = new Connection { RemoteEndpoint = _server.Endpoint };
        }

        [Test]
        public void Router_BadPath()
        {
            _router.Mount("/", _failDispatcher);
            string badPath = "/a/b/c/d/e/f/g/h/i/j/k/l/m/n/o/p/q";
            var greeter = GreeterPrx.FromConnection(_connection, badPath);
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

            var greeter = GreeterPrx.FromConnection(_connection, path);
            await greeter.IcePingAsync();
            Assert.AreEqual(1, value);

            // If we unmap the route we get ServiceNotFoundException
            Assert.That(_router.Unmap(path), Is.True);
            Assert.That(_router.Unmount(path), Is.True);
            Assert.ThrowsAsync<ServiceNotFoundException>(async () => await greeter.IcePingAsync());

            // Without exact match from Map, we hit the mounted route:
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
            var greeter = GreeterPrx.FromConnection(_connection, path);
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

            var greeter = GreeterPrx.FromConnection(_connection, path);
            await greeter.IcePingAsync();
            Assert.That(called, Is.True);

            Assert.That(_router.Unmount(prefix), Is.True);
            Assert.ThrowsAsync<ServiceNotFoundException>(async () => await greeter.IcePingAsync());
        }

        [TestCase("/foo", "/foobar")]
        [TestCase("/foo/bar", "/foo//bar")]
        public void Router_MountNotFound(string registered, string path)
        {
            _router.Mount(registered, _failDispatcher);
            var greeter = GreeterPrx.FromConnection(_connection, path);
            Assert.ThrowsAsync<ServiceNotFoundException>(async () => await greeter.IcePingAsync());
        }

        [TestCase("/foo", "/foo/bar", "/bar")]
        [TestCase("/foo/", "/foo/bar", "/bar")]
        [TestCase("/foo/bar///", "/foo/bar/", "/")]
        [TestCase("/foo///bar/a", "/foo///bar/a/b/c/d", "/b/c/d")]
        public async Task Router_RouteAsync(string prefix, string path, string subpath)
        {
            Assert.That(_router.AbsolutePrefix, Is.Empty);
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

            var greeter = GreeterPrx.FromConnection(_connection, path);
            await greeter.IcePingAsync();
            Assert.That(mainRouterMiddlewareCalled, Is.True);
            Assert.That(subRouterMiddlewareCalled, Is.True);
        }

        // Same test as above with one more level of nesting
        [TestCase("/foo", "/bar", "/foo/bar/abc", "/abc")]
        [TestCase("/foo/", "/bar/", "/foo/bar/abc", "/abc")]
        public async Task Router_RouteNestedAsync(string prefix, string subprefix, string path, string subpath)
        {
            Assert.That(_router.AbsolutePrefix, Is.Empty);

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

            var greeter = GreeterPrx.FromConnection(_connection, path);
            await greeter.IcePingAsync();
            Assert.That(mainRouterMiddlewareCalled, Is.True);
            Assert.That(nestedRouterMiddlewareCalled, Is.True);
        }

        [Test]
        public async Task Router_RouteDefaultPathAsync()
        {
            Assert.That(_router.AbsolutePrefix, Is.Empty);

            Assert.ThrowsAsync<ServiceNotFoundException>(
                async () => await GreeterPrx.FromConnection(_connection).IcePingAsync());
            _router.Map<IGreeter>(new Greeter());
            await GreeterPrx.FromConnection(_connection).IcePingAsync();

            _router.Unmap<IGreeter>();
            Assert.ThrowsAsync<ServiceNotFoundException>(
                async () => await GreeterPrx.FromConnection(_connection).IcePingAsync());

            Assert.ThrowsAsync<ServiceNotFoundException>(
                async () => await BaseAPrx.FromConnection(_connection).IcePingAsync());
            _router.Map<IBaseA>(new BaseA());
            await BaseAPrx.FromConnection(_connection).IcePingAsync();

            _router.Unmap<IBaseA>();
            Assert.ThrowsAsync<ServiceNotFoundException>(
                async () => await BaseAPrx.FromConnection(_connection).IcePingAsync());

            Assert.ThrowsAsync<ServiceNotFoundException>(
                async () => await DerivedAPrx.FromConnection(_connection).IcePingAsync());
            _router.Map<IDerivedA>(new DerivedA());
            await DerivedAPrx.FromConnection(_connection).IcePingAsync();

            _router.Unmap<IDerivedA>();
            Assert.ThrowsAsync<ServiceNotFoundException>(
                async () => await DerivedAPrx.FromConnection(_connection).IcePingAsync());

            Assert.ThrowsAsync<ServiceNotFoundException>(
                async () => await MostDerivedAPrx.FromConnection(_connection).IcePingAsync());
            _router.Map<IMostDerivedA>(new MostDerivedA());
            await MostDerivedAPrx.FromConnection(_connection).IcePingAsync();

            Assert.ThrowsAsync<ServiceNotFoundException>(
                async () => await BaseBPrx.FromConnection(_connection).IcePingAsync());
            _router.Map<IBaseB>(new BaseB());
            await BaseBPrx.FromConnection(_connection).IcePingAsync();

            _router.Unmap<IBaseB>();
            Assert.ThrowsAsync<ServiceNotFoundException>(
                async () => await BaseBPrx.FromConnection(_connection).IcePingAsync());

            Assert.ThrowsAsync<ServiceNotFoundException>(
                async () => await DerivedBPrx.FromConnection(_connection).IcePingAsync());
            _router.Map<IDerivedB>(new DerivedB());
            await DerivedBPrx.FromConnection(_connection).IcePingAsync();

            Assert.ThrowsAsync<ServiceNotFoundException>(
                async () => await MostDerivedBPrx.FromConnection(_connection).IcePingAsync());
            _router.Map<IMostDerivedB>(new MostDerivedB());
            await MostDerivedBPrx.FromConnection(_connection).IcePingAsync();

            _router.Unmap<IMostDerivedB>();
            Assert.ThrowsAsync<ServiceNotFoundException>(
                async () => await MostDerivedBPrx.FromConnection(_connection).IcePingAsync());

            Assert.ThrowsAsync<ServiceNotFoundException>(
                async () => await BaseCPrx.FromConnection(_connection).IcePingAsync());
            _router.Map<IBaseC>(new BaseC());
            await BaseCPrx.FromConnection(_connection).IcePingAsync();

            _router.Unmap<IBaseC>();
            Assert.ThrowsAsync<ServiceNotFoundException>(
             async () => await BaseCPrx.FromConnection(_connection).IcePingAsync());

            Assert.ThrowsAsync<ServiceNotFoundException>(
                async () => await DerivedCPrx.FromConnection(_connection).IcePingAsync());
            _router.Map<IDerivedC>(new DerivedC());
            await DerivedCPrx.FromConnection(_connection).IcePingAsync();

            Assert.ThrowsAsync<ServiceNotFoundException>(
                async () => await MostDerivedCPrx.FromConnection(_connection).IcePingAsync());
            _router.Map<IMostDerivedC>(new MostDerivedC());
            await MostDerivedCPrx.FromConnection(_connection).IcePingAsync();

            _router.Unmap<IMostDerivedC>();
            Assert.ThrowsAsync<ServiceNotFoundException>(
                async () => await MostDerivedCPrx.FromConnection(_connection).IcePingAsync());
        }

        [TearDown]
        public async ValueTask DisposeAsync()
        {
            await _server.DisposeAsync();
            await _connection.DisposeAsync();
        }

        public class Greeter : Service, IGreeter
        {
            public ValueTask SayHelloAsync(Dispatch dispatch, CancellationToken cancel) =>
                throw new NotImplementedException();
        }

        public class BaseA : Service, IBaseA { }
        public class DerivedA : Service, IDerivedA { }
        public class MostDerivedA : Service, IMostDerivedA { }

        public class BaseB : Service, IBaseB { }
        public class DerivedB : Service, IDerivedB { }
        public class MostDerivedB : Service, IMostDerivedB { }

        public class BaseC : Service, IBaseC { }
        public class DerivedC : Service, IDerivedC { }
        public class MostDerivedC : Service, IMostDerivedC { }
    }
}
