// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Interop;
using NUnit.Framework;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.ClientServer
{
    // Tests Interceptor.Locator
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    [Parallelizable(ParallelScope.All)]
    [Timeout(30000)]
    public sealed class LocatorTests : IAsyncDisposable
    {
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
            _server = new Server
            {
                Dispatcher = router,
                Endpoint = "tcp -h 127.0.0.1 -p 0"
            };

            _server.Listen();

            // Must be created after Listen to get the port number.
            _greeter = GreeterPrx.FromPath(path, Protocol.Ice1);
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
            var greeter = GreeterPrx.Parse(proxy, _pipeline);
            var indirectGreeter = GreeterPrx.Parse($"{greeter.Proxy.GetIdentity()} @ {adapter}", _pipeline);

            ISimpleLocatorTestPrx locator = CreateLocator();
            _pipeline.Use(Interceptors.Locator(locator));
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
            _pipeline.Use(Interceptors.Binder(_pool));

            await locator.RegisterAdapterAsync(adapter, greeter);

            CollectionAssert.IsEmpty(indirectGreeter.Proxy.AltEndpoints);
            Assert.AreEqual(Transport.Loc, indirectGreeter.Proxy.Endpoint!.Transport);

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
        public async Task Locator_Cache(int cacheMaxSize)
        {
            var indirectGreeter = GreeterPrx.Parse($"{_greeter.Proxy.GetIdentity()} @ adapt", _pipeline);
            var wellKnownGreeter = GreeterPrx.Parse(_greeter.Proxy.GetIdentity().ToString(), _pipeline);

            ISimpleLocatorTestPrx locator = CreateLocator();
            _pipeline.Use(Interceptors.Retry(2));
            _pipeline.Use(Interceptors.Locator(locator,
                                              new Interceptors.LocatorOptions
                                              {
                                                  CacheMaxSize = cacheMaxSize,
                                                  JustRefreshedAge = TimeSpan.Zero
                                              }));
            _pipeline.Use(next => new InlineInvoker(
                (request, cancel) =>
                {
                    // Only test if the resolution was successful
                    if (request.Proxy == indirectGreeter.Proxy && request.Endpoint?.Transport != Transport.Loc)
                    {
                        Assert.AreEqual(_greeter.Proxy.Endpoint, request.Endpoint);
                        _called = true;
                    }
                    else if (request.Proxy == wellKnownGreeter.Proxy && request.Endpoint != null)
                    {
                        Assert.AreEqual(_greeter.Proxy.Endpoint, request.Endpoint);
                        _called = true;
                    }
                    return next.InvokeAsync(request, cancel);
                }));

            // We don't cache the connection in order to use the locator interceptor for each invocation.
            _pipeline.Use(Interceptors.Binder(_pool, cacheConnection: false));

            Assert.ThrowsAsync<NoEndpointException>(async () => await indirectGreeter.SayHelloAsync());
            Assert.That(_called, Is.False);
            await locator.RegisterAdapterAsync("adapt", _greeter);
            Assert.DoesNotThrowAsync(async () => await indirectGreeter.SayHelloAsync());
            Assert.That(_called, Is.True);
            _called = false;

            Assert.That(await locator.UnregisterAdapterAsync("adapt"), Is.True);

            // We still find it in the cache and can still call it
            Assert.DoesNotThrowAsync(async () => await indirectGreeter.SayHelloAsync());

            // Force a retry to get re-resolution
            Assert.ThrowsAsync<ServiceNotFoundException>(async () => await indirectGreeter.SayHelloAsync(
                new Invocation
                {
                    Context = new SortedDictionary<string, string>
                    {
                        ["retry"] = "yes"
                    }
                }));
            Assert.ThrowsAsync<NoEndpointException>(async () => await indirectGreeter.SayHelloAsync());

            // Same with well-known greeter

            Assert.ThrowsAsync<NoEndpointException>(async () => await wellKnownGreeter.SayHelloAsync());
            await locator.RegisterWellKnownProxyAsync(_greeter.Proxy.GetIdentity(), indirectGreeter);
            await locator.RegisterAdapterAsync("adapt", _greeter);
            _called = false;
            Assert.DoesNotThrowAsync(async () => await wellKnownGreeter.SayHelloAsync());
            Assert.That(_called, Is.True);

            Assert.That(await locator.UnregisterWellKnownProxyAsync(_greeter.Proxy.GetIdentity()), Is.True);

            if (cacheMaxSize > 1)
            {
                // We still find it in the cache and can still call it.
                Assert.DoesNotThrowAsync(async () => await wellKnownGreeter.SayHelloAsync());

                // Force a retry to get re-resolution.
                Assert.ThrowsAsync<ServiceNotFoundException>(async () => await wellKnownGreeter.SayHelloAsync(
                    new Invocation
                    {
                        Context = new SortedDictionary<string, string>
                        {
                            ["retry"] = "yes"
                        }
                    }));
            }

            Assert.ThrowsAsync<NoEndpointException>(async () => await wellKnownGreeter.SayHelloAsync());
        }

        [TestCase("foo:tcp -h host1 -p 10000")]
        [TestCase("bar:ssl -h host1 -p 10000:udp -h host2 -p 20000")]
        [TestCase("cat/xyz:ssl -h host1 -p 10000 -z")]
        /// <summary>Verifies the interceptor works properly for well-known proxies.</summary>
        public async Task Locator_WellKnownProxyResolveAsync(string proxy)
        {
            // There is no corresponding service, we're just testing the endpoints.
            var greeter = GreeterPrx.Parse(proxy, _pipeline);
            Identity identity = greeter.Proxy.GetIdentity();

            var wellKnownGreeter = GreeterPrx.Parse(identity.ToString(), _pipeline);
            Assert.That(wellKnownGreeter.Proxy.Endpoint, Is.Null);

            ISimpleLocatorTestPrx locator = CreateLocator();
            _pipeline.Use(Interceptors.Locator(locator));
            _pipeline.Use(next => new InlineInvoker(
                (request, cancel) =>
                {
                    if (request.Proxy.Endpoint == null && request.GetIdentity() == identity)
                    {
                        Assert.AreEqual(greeter.Proxy.Endpoint, request.Endpoint);
                        _called = true;
                    }
                    return next.InvokeAsync(request, cancel);
                }));
            _pipeline.Use(Interceptors.Binder(_pool));

            // Test with direct endpoints
            await locator.RegisterWellKnownProxyAsync(identity, greeter);
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
            var indirectGreeter = GreeterPrx.Parse($"{identity} @ '{adapter}'", _pipeline);
            Assert.AreEqual($"loc -h {adapter} -p 0", indirectGreeter.Proxy.Endpoint?.ToString());

            await locator.RegisterAdapterAsync(adapter, greeter);

            Assert.That(await locator.UnregisterWellKnownProxyAsync(identity), Is.True);
            await locator.RegisterWellKnownProxyAsync(identity, indirectGreeter);

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

        private ISimpleLocatorTestPrx CreateLocator()
        {
            string path = $"/{Guid.NewGuid()}";
            (_server.Dispatcher as Router)!.Map(path, new Locator());

            var locator = SimpleLocatorTestPrx.FromPath(path, _server.Endpoint.Protocol);
            locator.Proxy.Endpoint = _server.Endpoint;
            locator.Proxy.Invoker = _pipeline;
            return locator;
        }

        private class Locator : Service, ISimpleLocatorTest
        {
            private readonly IDictionary<string, ServicePrx> _adapterMap =
                new ConcurrentDictionary<string, ServicePrx>();
            private readonly IDictionary<Identity, ServicePrx> _identityMap =
                new ConcurrentDictionary<Identity, ServicePrx>();

            public ValueTask<ServicePrx?> FindObjectByIdAsync(
                Identity id,
                Dispatch dispatch,
                CancellationToken cancel) =>
                new(_identityMap.TryGetValue(id, out ServicePrx value) ? value : null);

            public ValueTask<ServicePrx?> FindAdapterByIdAsync(string id, Dispatch dispatch, CancellationToken cancel) =>
                new(_adapterMap.TryGetValue(id, out ServicePrx value) ? value : null);

            public ValueTask<LocatorRegistryPrx?> GetRegistryAsync(Dispatch dispatch, CancellationToken cancel)
            {
                Assert.Fail("unexpected call to GetRegistryAsync");
                return new(null as LocatorRegistryPrx?);
            }

            public ValueTask RegisterAdapterAsync(
                string adapter,
                ServicePrx dummy,
                Dispatch dispatch,
                CancellationToken cancel)
            {
                _adapterMap.Add(adapter, dummy);
                return default;
            }

            public ValueTask RegisterWellKnownProxyAsync(
                Identity identity,
                ServicePrx dummy,
                Dispatch dispatch,
                CancellationToken cancel)
            {
                _identityMap.Add(identity, dummy);
                return default;
            }

            public ValueTask<bool> UnregisterAdapterAsync(string adapter, Dispatch dispatch, CancellationToken cancel) =>
                new(_adapterMap.Remove(adapter));

            public ValueTask<bool> UnregisterWellKnownProxyAsync(
                Identity identity,
                Dispatch dispatch,
                CancellationToken cancel) =>
                new(_identityMap.Remove(identity));
        }

        private class Greeter : Service, IGreeter
        {
            public ValueTask SayHelloAsync(Dispatch dispatch, CancellationToken cancel)
            {
                if (dispatch.Context.ContainsKey("retry"))
                {
                    // Other replica so that the retry interceptor clears the connection
                    // We have to use ServiceNotFoundException because we use ice1.
                    throw new ServiceNotFoundException(RetryPolicy.OtherReplica);
                }
                return default;
            }
        }
    }
}
