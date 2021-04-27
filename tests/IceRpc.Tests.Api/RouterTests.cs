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

        private static IDispatcher _service = new GreeterService();

        private Communicator _communicator;

        private Router _router = new Router();
        private Server _server;

        public RouterTests()
        {
            _communicator = new Communicator();
            _server = new Server
            {
                Communicator = _communicator,
                Dispatcher = _router,
                Endpoint = TestHelper.GetUniqueColocEndpoint()
            };
            _server.Listen();
        }

        [Test]
        public void Router_BadPath()
        {
            _router.Mount("/", _failDispatcher);
            string badPath = "/a/b/c/d/e/f/g/h/i/j/k/l/m/n/o/p/q";
            Assert.ThrowsAsync<ServerException>(async () => await GetGreeter(badPath).IcePingAsync());

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

            IGreeterServicePrx greeter = GetGreeter(path);
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
            Assert.ThrowsAsync<ServiceNotFoundException>(async () => await GetGreeter(path).IcePingAsync());
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

            await GetGreeter(path).IcePingAsync();
            Assert.IsTrue(called);
        }

        [TestCase("/foo", "/foobar")]
        [TestCase("/foo/bar", "/foo//bar")]
        public void Router_MountNotFound(string registered, string path)
        {
            _router.Mount(registered, _failDispatcher);
            Assert.ThrowsAsync<ServiceNotFoundException>(async () => await GetGreeter(path).IcePingAsync());
        }

        [TestCase("/foo", "/foo/bar", "/bar")]
        [TestCase("/foo/", "/foo/bar", "/bar")]
        [TestCase("/foo/bar///", "/foo/bar/", "/")]
        [TestCase("/foo///bar/a", "/foo///bar/a/b/c/d", "/b/c/d")]
        public async Task Router_RouteAsync(string prefix, string path, string subpath)
        {
            Assert.IsEmpty(_router.AbsolutePrefix);
            bool called = false;
            _router.Route(prefix, r =>
                {
                    Assert.AreEqual(prefix.TrimEnd('/'), r.AbsolutePrefix);
                    r.Map(subpath, new InlineDispatcher(
                        async (current, cancel) =>
                        {
                            called = true;
                            Assert.AreEqual(path, current.Path);
                            Assert.AreEqual($"{r.AbsolutePrefix}{subpath}", current.Path);
                            return await _service.DispatchAsync(current, cancel);
                        }));
                });

            await GetGreeter(path).IcePingAsync();
            Assert.IsTrue(called);
        }

        // Same test as above with one more level of nesting
        [TestCase("/foo", "/bar", "/foo/bar/abc", "/abc")]
        [TestCase("/foo/", "/bar/", "/foo/bar/abc", "/abc")]
        public async Task Router_RouteNestedAsync(string prefix, string subprefix, string path, string subpath)
        {
            Assert.IsEmpty(_router.AbsolutePrefix);
            bool called = false;
            _router.Route(prefix, r =>
                {
                    Assert.AreEqual(prefix.TrimEnd('/'), r.AbsolutePrefix);
                    r.Route(subprefix, r =>
                    {
                        r.Map(subpath, new InlineDispatcher(
                            async (current, cancel) =>
                            {
                                called = true;
                                Assert.AreEqual(path, current.Path);
                                Assert.AreEqual($"{r.AbsolutePrefix}{subpath}", current.Path);
                                return await _service.DispatchAsync(current, cancel);
                            }));
                    });
                });

            await GetGreeter(path).IcePingAsync();
            Assert.IsTrue(called);
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            await _server.ShutdownAsync();
            await _communicator.ShutdownAsync();
        }

        private IGreeterServicePrx GetGreeter(string path) =>
            _server.CreateProxy<IGreeterServicePrx>(path);

        public class GreeterService : IAsyncGreeterService
        {
            public ValueTask SayHelloAsync(Dispatch dispatch, CancellationToken cancel) =>
                throw new NotImplementedException();
        }
    }
}
