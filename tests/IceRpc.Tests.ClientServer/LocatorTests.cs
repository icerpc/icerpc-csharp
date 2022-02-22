// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Features;
using IceRpc.Slice;
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
        private string GreeterPath => _service.Proxy.Path;

        private bool _called;
        private readonly ConnectionPool _pool = new();
        private readonly ServicePrx _service;

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
            _service = ServicePrx.Parse($"ice:{path}");
            _service.Proxy.Endpoint = _server.Endpoint;
            _service.Proxy.Invoker = _pipeline;
        }

        [TestCase("adapt1", "foo:tcp -h host1 -p 10000")]
        [TestCase("adapt2", "bar:ssl -h host1 -p 10000:udp -h host2 -p 20000")]
        [TestCase("adapt3", "xyz:ssl -h host1 -p 10000 -z")]
        /// <summary>Verifies the interceptor works properly for proxies with an @ adapter endpoint.</summary>
        public async Task Locator_AdapterResolveAsync(string adapter, string proxy)
        {
            // There is no corresponding service, we're just testing the endpoints.
            var service = ServicePrx.Parse(proxy, _pipeline, IceProxyFormat.Default);
            var greeterIdentity = service.Proxy.Path[1..];

            var indirectService = ServicePrx.Parse(
                $"{greeterIdentity} @ {adapter}",
                _pipeline,
                IceProxyFormat.Default);

            var locator = new FakeLocatorPrx();
            _pipeline.UseLocator(locator, new() { LoggerFactory = LogAttributeLoggerFactory.Instance });
            _pipeline.Use(next => new InlineInvoker(
                (request, cancel) =>
                {
                    if (request.Proxy == indirectService.Proxy)
                    {
                        EndpointSelection? endpointSelection = request.Features.Get<EndpointSelection>();
                        Assert.That(endpointSelection, Is.Not.Null);
                        Assert.AreEqual(service.Proxy.Endpoint, endpointSelection.Endpoint);
                        _called = true;
                    }
                    return next.InvokeAsync(request, cancel);
                }));
            _pipeline.UseBinder(_pool);

            locator.RegisterAdapter(adapter, service);

            CollectionAssert.IsEmpty(indirectService.Proxy.AltEndpoints);

            ServicePrx? found = await locator.FindAdapterByIdAsync(adapter);
            Assert.That(found, Is.Not.Null);
            Assert.AreEqual(found?.Proxy.Endpoint, service.Proxy.Endpoint);

            Assert.That(_called, Is.False);
            try
            {
                await indirectService.IcePingAsync();
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
            var indirectGreeter = GreeterPrx.Parse($"{GreeterPath} @ adapt", _pipeline, IceProxyFormat.Default);
            var wellKnownGreeter = GreeterPrx.Parse(GreeterPath.ToString(), _pipeline, IceProxyFormat.Default);

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
                    EndpointSelection? endpointSelection = request.Features.Get<EndpointSelection>();
                    if (endpointSelection?.Endpoint != null)
                    {
                        if (request.Proxy == indirectGreeter.Proxy || request.Proxy == wellKnownGreeter.Proxy)
                        {
                            Assert.AreEqual(_service.Proxy.Endpoint, endpointSelection.Endpoint);
                            _called = true;
                        }
                    }
                    return next.InvokeAsync(request, cancel);
                }));

            // We don't cache the connection in order to use the locator interceptor for each invocation.
            _pipeline.UseBinder(_pool, cacheConnection: false);

            Assert.ThrowsAsync<NoEndpointException>(async () => await indirectGreeter.SayHelloAsync("hello"));
            Assert.That(_called, Is.False);
            locator.RegisterAdapter("adapt", _service);
            Assert.DoesNotThrowAsync(async () => await indirectGreeter.SayHelloAsync("hello"));
            Assert.That(_called, Is.True);
            _called = false;

            Assert.That(locator.UnregisterAdapter("adapt"), Is.True);

            // We still find it in the cache and can still call it
            Assert.DoesNotThrowAsync(async () => await indirectGreeter.SayHelloAsync("hello"));

            // Force a retry to get re-resolution
            var dispatchException = Assert.ThrowsAsync<DispatchException>(() => indirectGreeter.SayHelloAsync(
                "hello",
                new Invocation
                {
                    Features = new FeatureCollection().WithContext(
                        new Dictionary<string, string> { ["retry"] = "yes" })
                }));

            Assert.That(dispatchException!.ErrorCode, Is.EqualTo(DispatchErrorCode.ServiceNotFound));

            Assert.ThrowsAsync<NoEndpointException>(() => indirectGreeter.SayHelloAsync("hello"));

            // Same with well-known greeter

            Assert.ThrowsAsync<NoEndpointException>(() => wellKnownGreeter.SayHelloAsync("hello"));
            locator.RegisterWellKnownProxy(GreeterPath, new ServicePrx(indirectGreeter.Proxy));
            locator.RegisterAdapter("adapt", _service);
            _called = false;
            Assert.DoesNotThrowAsync(() => wellKnownGreeter.SayHelloAsync("hello"));
            Assert.That(_called, Is.True);

            Assert.That(locator.UnregisterWellKnownProxy(GreeterPath), Is.True);

            if (cacheMaxSize > 1)
            {
                // We still find it in the cache and can still call it.
                Assert.DoesNotThrowAsync(() => wellKnownGreeter.SayHelloAsync("hello"));

                // Force a retry to get re-resolution.
                dispatchException = Assert.ThrowsAsync<DispatchException>(() => wellKnownGreeter.SayHelloAsync(
                    "hello",
                    new Invocation
                    {
                        Features = new FeatureCollection().WithContext(
                            new Dictionary<string, string> { ["retry"] = "yes" })
                    }));
                Assert.That(dispatchException!.ErrorCode, Is.EqualTo(DispatchErrorCode.ServiceNotFound));
            }

            Assert.ThrowsAsync<NoEndpointException>(() => wellKnownGreeter.SayHelloAsync("hello"));
        }

        [TestCase("foo:tcp -h host1 -p 10000")]
        [TestCase("bar:ssl -h host1 -p 10000:udp -h host2 -p 20000")]
        [TestCase("cat/xyz:ssl -h host1 -p 10000 -z")]
        /// <summary>Verifies the interceptor works properly for well-known proxies.</summary>
        public async Task Locator_WellKnownProxyResolveAsync(string proxy)
        {
            // There is no corresponding service, we're just testing the endpoints.
            var service = ServicePrx.Parse(proxy, _pipeline, IceProxyFormat.Default);
            string identity = GreeterPath[1..]; // usually the stringified identity is just the path less leading /

            var wellKnownService = ServicePrx.Parse(identity, _pipeline, IceProxyFormat.Default);
            Assert.That(wellKnownService.Proxy.Endpoint, Is.Null);

            var locator = new FakeLocatorPrx();
            _pipeline.UseLocator(locator, new() { LoggerFactory = LogAttributeLoggerFactory.Instance });
            _pipeline.Use(next => new InlineInvoker(
                (request, cancel) =>
                {
                    if (request.Proxy.Endpoint == null && request.Proxy.Path == _service.Proxy.Path)
                    {
                        EndpointSelection? endpointSelection = request.Features.Get<EndpointSelection>();
                        Assert.That(endpointSelection, Is.Not.Null);
                        Assert.AreEqual(service.Proxy.Endpoint, endpointSelection.Endpoint);
                        _called = true;
                    }
                    return next.InvokeAsync(request, cancel);
                }));
            _pipeline.UseBinder(_pool);

            // Test with direct endpoints
            locator.RegisterWellKnownProxy(GreeterPath, service);
            ServicePrx? found = await locator.FindObjectByIdAsync(GreeterPath);
            Assert.That(found, Is.Not.Null);
            Assert.AreEqual(found?.Proxy.Endpoint, service.Proxy.Endpoint);

            Assert.That(_called, Is.False);
            try
            {
                await wellKnownService.IcePingAsync();
            }
            catch
            {
                // ignored
            }
            Assert.That(_called, Is.True);
            _called = false;

            // Test with indirect endpoints
            string adapter = $"adapter/{identity}";
            var indirectService = ServicePrx.Parse($"{identity} @ '{adapter}'", _pipeline, IceProxyFormat.Default);

            locator.RegisterAdapter(adapter, service);

            Assert.That(locator.UnregisterWellKnownProxy(GreeterPath), Is.True);
            locator.RegisterWellKnownProxy(GreeterPath, indirectService);

            found = await locator.FindObjectByIdAsync(GreeterPath);
            Assert.That(found, Is.Not.Null);
            Assert.AreEqual(indirectService.Proxy.Endpoint, found?.Proxy.Endpoint); // partial resolution

            Assert.That(_called, Is.False);
            try
            {
                await wellKnownService.IcePingAsync();
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
            private readonly IDictionary<string, ServicePrx> _wellKnownMap =
                new ConcurrentDictionary<string, ServicePrx>();

            public Task<ServicePrx?> FindObjectByIdAsync(
                string id,
                Invocation? invocation = null,
                CancellationToken cancel = default) =>
                Task.FromResult<ServicePrx?>(_wellKnownMap.TryGetValue(id, out ServicePrx value) ? value : null);

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

            internal void RegisterWellKnownProxy(string path, ServicePrx dummy) =>
                _wellKnownMap.Add(path, dummy);

            internal bool UnregisterAdapter(string adapter) => _adapterMap.Remove(adapter);
            internal bool UnregisterWellKnownProxy(string path) => _wellKnownMap.Remove(path);
        }

        private class Greeter : Service, IGreeter
        {
            public ValueTask SayHelloAsync(string message, Dispatch dispatch, CancellationToken cancel)
            {
                if (dispatch.Features.GetContext().ContainsKey("retry"))
                {
                    // Other replica so that the retry interceptor clears the connection
                    // We have to use DispatchException(ServiceNotFound) because we use ice.
                    throw new DispatchException(DispatchErrorCode.ServiceNotFound, RetryPolicy.OtherReplica);
                }
                return default;
            }
        }
    }
}
