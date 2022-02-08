// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using NUnit.Framework;
using System.Collections.Concurrent;

namespace IceRpc.Tests.ClientServer
{
    // Tests Interceptor.Locator
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    [Parallelizable(ParallelScope.All)]
    [Timeout(30000)]
    // [Log(LogAttributeLevel.Trace)]
    public sealed class LocatorTests : IAsyncDisposable
    {
        private Identity GreeterIdentity => Identity.FromPath(_greeter.Proxy.Path);

        private bool _called;
        private readonly ConnectionPool _pool = new();
        private readonly GreeterPrx _greeter;

        private readonly Pipeline _pipeline = new();
        private readonly Server _server;

        public LocatorTests()
        {
            var router = new Router();
            string path = $"/{Guid.NewGuid()}";
            router.Map(path, new Greeter());
            string serverEndpoint = "ice://127.0.0.1:0?tls=false";
            _server = new Server
            {
                Dispatcher = router,
                Endpoint = serverEndpoint
            };

            _server.Listen();

            // Must be created after Listen to get the port number.
            _greeter = GreeterPrx.Parse($"ice:{path}");
            _greeter.Proxy.Endpoint = _server.Endpoint;
            _greeter.Proxy.Invoker = _pipeline;
        }

        [TestCase("adapt1", "foo:tcp -h host1 -p 10000")]
        [TestCase("adapt2", "bar:ssl -h host1 -p 10000:udp -h host2 -p 20000")]
        [TestCase("adapt3", "xyz:ssl -h host1 -p 10000 -z")]
        /// <summary>Verifies the interceptor works properly for proxies with an @ adapter endpoint.</summary>
        public async Task Locator_AdapterResolveAsync(string adapter, string proxy)
        {
            // There is no corresponding service, we're just testing the endpoints.
            var greeter = GreeterPrx.Parse(proxy, _pipeline, IceProxyFormat.Default);
            var greeterIdentity = Identity.FromPath(greeter.Proxy.Path);

            var indirectGreeter = GreeterPrx.Parse(
                $"{greeterIdentity} @ {adapter}",
                _pipeline,
                IceProxyFormat.Default);

            var locator = new FakeLocatorPrx();
            _pipeline.UseLocator(locator, new() { LoggerFactory = LogAttributeLoggerFactory.Instance });
            _pipeline.Use(next => new InlineInvoker(
                (request, cancel) =>
                {
                    if (request.Proxy == indirectGreeter.Proxy)
                    {
                        Assert.AreEqual(greeter.Proxy.Endpoint, request.Endpoint);
                        _called = true;
                    }
                    return next.InvokeAsync(request, cancel);
                }));
            _pipeline.UseBinder(_pool);

            locator.RegisterAdapter(adapter, greeter);

            CollectionAssert.IsEmpty(indirectGreeter.Proxy.AltEndpoints);

            ServicePrx? found = await locator.FindAdapterByIdAsync(adapter);
            Assert.That(found, Is.Not.Null);
            Assert.AreEqual(found?.Proxy.Endpoint, greeter.Proxy.Endpoint);

            Assert.That(_called, Is.False);
            try
            {
                await indirectGreeter.IcePingAsync();
            }
            catch
            {
                // ignored
            }
            Assert.That(_called, Is.True);
        }

        [TestCase(1)]
        [TestCase(2)]
        /// <summary>Makes sure a locator interceptor caches resolutions.</summary>
        public void Locator_Cache(int cacheMaxSize)
        {
            var indirectGreeter = GreeterPrx.Parse($"{GreeterIdentity} @ adapt", _pipeline, IceProxyFormat.Default);
            var wellKnownGreeter = GreeterPrx.Parse(GreeterIdentity.ToString(), _pipeline, IceProxyFormat.Default);

            var locator = new FakeLocatorPrx();
            _pipeline.UseRetry(new RetryOptions { MaxAttempts = 2 });
            _pipeline.UseLocator(
                locator,
                new LocatorOptions
                {
                    CacheMaxSize = cacheMaxSize,
                    JustRefreshedAge = TimeSpan.Zero,
                    LoggerFactory = LogAttributeLoggerFactory.Instance
                });
            _pipeline.Use(next => new InlineInvoker(
                (request, cancel) =>
                {
                    // Only test if the resolution was successful
                    if (request.Endpoint != null)
                    {
                        if (request.Proxy == indirectGreeter.Proxy || request.Proxy == wellKnownGreeter.Proxy)
                        {
                            Assert.AreEqual(_greeter.Proxy.Endpoint, request.Endpoint);
                            _called = true;
                        }
                    }
                    return next.InvokeAsync(request, cancel);
                }));

            // We don't cache the connection in order to use the locator interceptor for each invocation.
            _pipeline.UseBinder(_pool, cacheConnection: false);

            Assert.ThrowsAsync<NoEndpointException>(async () => await indirectGreeter.SayHelloAsync("hello"));
            Assert.That(_called, Is.False);
            locator.RegisterAdapter("adapt", _greeter);
            Assert.DoesNotThrowAsync(async () => await indirectGreeter.SayHelloAsync("hello"));
            Assert.That(_called, Is.True);
            _called = false;

            Assert.That(locator.UnregisterAdapter("adapt"), Is.True);

            // We still find it in the cache and can still call it
            Assert.DoesNotThrowAsync(async () => await indirectGreeter.SayHelloAsync("hello"));

            // Force a retry to get re-resolution
            Assert.ThrowsAsync<ServiceNotFoundException>(async () => await indirectGreeter.SayHelloAsync(
                "hello",
                new Invocation
                {
                    Features = new FeatureCollection().WithContext(
                        new Dictionary<string, string> { ["retry"] = "yes" })
                }));
            Assert.ThrowsAsync<NoEndpointException>(async () => await indirectGreeter.SayHelloAsync("hello"));

            // Same with well-known greeter

            Assert.ThrowsAsync<NoEndpointException>(async () => await wellKnownGreeter.SayHelloAsync("hello"));
            locator.RegisterWellKnownProxy(GreeterIdentity, indirectGreeter);
            locator.RegisterAdapter("adapt", _greeter);
            _called = false;
            Assert.DoesNotThrowAsync(async () => await wellKnownGreeter.SayHelloAsync("hello"));
            Assert.That(_called, Is.True);

            Assert.That(locator.UnregisterWellKnownProxy(GreeterIdentity), Is.True);

            if (cacheMaxSize > 1)
            {
                // We still find it in the cache and can still call it.
                Assert.DoesNotThrowAsync(async () => await wellKnownGreeter.SayHelloAsync("hello"));

                // Force a retry to get re-resolution.
                Assert.ThrowsAsync<ServiceNotFoundException>(async () => await wellKnownGreeter.SayHelloAsync(
                    "hello",
                    new Invocation
                    {
                        Features = new FeatureCollection().WithContext(
                            new Dictionary<string, string> { ["retry"] = "yes" })
                    }));
            }

            Assert.ThrowsAsync<NoEndpointException>(async () => await wellKnownGreeter.SayHelloAsync("hello"));
        }

        [TestCase("foo:tcp -h host1 -p 10000")]
        [TestCase("bar:ssl -h host1 -p 10000:udp -h host2 -p 20000")]
        [TestCase("cat/xyz:ssl -h host1 -p 10000 -z")]
        /// <summary>Verifies the interceptor works properly for well-known proxies.</summary>
        public async Task Locator_WellKnownProxyResolveAsync(string proxy)
        {
            // There is no corresponding service, we're just testing the endpoints.
            var greeter = GreeterPrx.Parse(proxy, _pipeline, IceProxyFormat.Default);
            Identity identity = GreeterIdentity;

            var wellKnownGreeter = GreeterPrx.Parse(identity.ToString(), _pipeline, IceProxyFormat.Default);
            Assert.That(wellKnownGreeter.Proxy.Endpoint, Is.Null);

            var locator = new FakeLocatorPrx();
            _pipeline.UseLocator(locator, new() { LoggerFactory = LogAttributeLoggerFactory.Instance });
            _pipeline.Use(next => new InlineInvoker(
                (request, cancel) =>
                {
                    if (request.Proxy.Endpoint == null && request.Path == _greeter.Proxy.Path)
                    {
                        Assert.AreEqual(greeter.Proxy.Endpoint, request.Endpoint);
                        _called = true;
                    }
                    return next.InvokeAsync(request, cancel);
                }));
            _pipeline.UseBinder(_pool);

            // Test with direct endpoints
            locator.RegisterWellKnownProxy(identity, greeter);
            ServicePrx? found = await locator.FindObjectByIdAsync(identity);
            Assert.That(found, Is.Not.Null);
            Assert.AreEqual(found?.Proxy.Endpoint, greeter.Proxy.Endpoint);

            Assert.That(_called, Is.False);
            try
            {
                await wellKnownGreeter.IcePingAsync();
            }
            catch
            {
                // ignored
            }
            Assert.That(_called, Is.True);
            _called = false;

            // Test with indirect endpoints
            string adapter = $"adapter/{identity.Category}/{identity.Name}";
            var indirectGreeter = GreeterPrx.Parse($"{identity} @ '{adapter}'", _pipeline, IceProxyFormat.Default);

            locator.RegisterAdapter(adapter, greeter);

            Assert.That(locator.UnregisterWellKnownProxy(identity), Is.True);
            locator.RegisterWellKnownProxy(identity, indirectGreeter);

            found = await locator.FindObjectByIdAsync(identity);
            Assert.That(found, Is.Not.Null);
            Assert.AreEqual(indirectGreeter.Proxy.Endpoint, found?.Proxy.Endpoint); // partial resolution

            Assert.That(_called, Is.False);
            try
            {
                await wellKnownGreeter.IcePingAsync();
            }
            catch
            {
                // ignored
            }
            Assert.That(_called, Is.True);
        }

        [TearDown]
        public async ValueTask DisposeAsync()
        {
            await _server.DisposeAsync();
            await _pool.DisposeAsync();
        }

        // An implementation of the ILocatorPrx interface used for testing
        private class FakeLocatorPrx : ILocatorPrx
        {
            private readonly IDictionary<string, ServicePrx> _adapterMap =
                new ConcurrentDictionary<string, ServicePrx>();
            private readonly IDictionary<Identity, ServicePrx> _identityMap =
                new ConcurrentDictionary<Identity, ServicePrx>();

            public Task<ServicePrx?> FindObjectByIdAsync(
                Identity id,
                Invocation? invocation = null,
                CancellationToken cancel = default) =>
                Task.FromResult<ServicePrx?>(_identityMap.TryGetValue(id, out ServicePrx value) ? value : null);

            public Task<ServicePrx?> FindAdapterByIdAsync(
                string id,
                Invocation? invocation = null,
                CancellationToken cancel = default) =>
                Task.FromResult<ServicePrx?>(_adapterMap.TryGetValue(id, out ServicePrx value) ? value : null);

            public Task<LocatorRegistryPrx?> GetRegistryAsync(Invocation? invocation, CancellationToken cancel)
            {
                Assert.Fail("unexpected call to GetRegistryAsync");
                return Task.FromResult<LocatorRegistryPrx?>(null);
            }

            internal void RegisterAdapter(string adapterId, ServicePrx dummy) =>
                _adapterMap.Add(adapterId, dummy);

            internal void RegisterWellKnownProxy(Identity identity, ServicePrx dummy) =>
                _identityMap.Add(identity, dummy);

            internal bool UnregisterAdapter(string adapter) => _adapterMap.Remove(adapter);
            internal bool UnregisterWellKnownProxy(Identity identity) => _identityMap.Remove(identity);
        }

        private class Greeter : Service, IGreeter
        {
            public ValueTask SayHelloAsync(string message, Dispatch dispatch, CancellationToken cancel)
            {
                if (dispatch.Features.GetContext().ContainsKey("retry"))
                {
                    // Other replica so that the retry interceptor clears the connection
                    // We have to use ServiceNotFoundException because we use ice.
                    throw new ServiceNotFoundException(RetryPolicy.OtherReplica);
                }
                return default;
            }
        }
    }
}
