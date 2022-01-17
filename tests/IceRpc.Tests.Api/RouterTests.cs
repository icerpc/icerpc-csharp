// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Tests.Api
{
    // Each test case gets a fresh communicator, server and router.
    [Timeout(30000)]
    [Parallelizable(scope: ParallelScope.All)]
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    public sealed class RouterTests
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
        private readonly ServiceProvider _serviceProvider;

        public RouterTests()
        {
            _serviceProvider = new IntegrationTestServiceCollection()
                .AddTransient<IDispatcher>(_ => _router)
                .BuildServiceProvider();
            _connection = _serviceProvider.GetRequiredService<Connection>();
        }

        [TearDown]
        public ValueTask DisposeAsync() => _serviceProvider.DisposeAsync();

        [Test]
        public void Router_BadPath()
        {
            Assert.Throws<FormatException>(() => _router.Map("foo", _failDispatcher));
            Assert.Throws<FormatException>(() => _router.Mount("foo", _failDispatcher));

            _router.Mount("/", _failDispatcher);
            string badPath = "/a/b/c/d/e/f/g/h/i/j/k/l/m/n/o/p/q";
            var greeter = GreeterPrx.FromConnection(_connection, badPath);
            Assert.ThrowsAsync<DispatchException>(async () => await greeter.IcePingAsync());
        }

        [Test]
        public async Task Router_InvalidOperationAsync()
        {
            _router.Map<IGreeter>(new Greeter());
            await GreeterPrx.FromConnection(_connection).IcePingAsync();
            Assert.Throws<InvalidOperationException>(() => _router.Map<IGreeter>(new Greeter()));
            Assert.Throws<InvalidOperationException>(() => _router.Mount("/foo", new Greeter()));
            Assert.Throws<InvalidOperationException>(() => _router.Use(next => next));
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

            _router.Map<IGreeter>(new Greeter());
            _router.Map<IBaseA>(new BaseA());
            _router.Map<IDerivedA>(new DerivedA());
            _router.Map<IMostDerivedA>(new MostDerivedA());
            _router.Map<IBaseB>(new BaseB());
            _router.Map<IDerivedB>(new DerivedB());
            _router.Map<IMostDerivedB>(new MostDerivedB());
            _router.Map<IBaseC>(new BaseC());
            _router.Map<IDerivedC>(new DerivedC());
            _router.Map<IMostDerivedC>(new MostDerivedC());

            await GreeterPrx.FromConnection(_connection).IcePingAsync();
            await BaseAPrx.FromConnection(_connection).IcePingAsync();
            await DerivedAPrx.FromConnection(_connection).IcePingAsync();
            await MostDerivedAPrx.FromConnection(_connection).IcePingAsync();
            await BaseBPrx.FromConnection(_connection).IcePingAsync();
            await DerivedBPrx.FromConnection(_connection).IcePingAsync();
            await MostDerivedBPrx.FromConnection(_connection).IcePingAsync();
            await BaseCPrx.FromConnection(_connection).IcePingAsync();
            await DerivedCPrx.FromConnection(_connection).IcePingAsync();
            await MostDerivedCPrx.FromConnection(_connection).IcePingAsync();
        }

        public class Greeter : Service, IGreeter
        {
            public ValueTask SayHelloAsync(string message, Dispatch dispatch, CancellationToken cancel) =>
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
