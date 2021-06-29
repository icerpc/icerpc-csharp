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
    public class LocatorTests
    {
        private bool _called;
        private readonly ConnectionPool _pool = new();
        private readonly IGreeterPrx _greeter;

        private readonly Pipeline _pipeline = new();
        private readonly Server _server;

        public LocatorTests()
        {
            var router = new Router();
            string path = $"/{Guid.NewGuid()}";
            router.Map(path, new Greeter());
            _server = new Server
            {
                HasColocEndpoint = false,
                Dispatcher = router,
                Endpoint = "tcp -h 127.0.0.1 -p 0",
                // TODO use localhost see https://github.com/dotnet/runtime/issues/53447
                HostName = "127.0.0.1"
            };

            _server.Listen();

            // Must be created after Listen to get the port number.
            _greeter = IGreeterPrx.FromServer(_server, path);
            _greeter.Invoker = _pipeline;
        }

        [TestCase("adapt1", "foo:tcp -h host1 -p 10000")]
        [TestCase("adapt2", "bar:ssl -h host1 -p 10000:udp -h host2 -p 20000")]
        [TestCase("adapt3", "xyz:ssl -h host1 -p 10000 -z")]
        /// <summary>Verifies the interceptor works properly for proxies with an @ adapter endpoint.</summary>
        public async Task Locator_AdapterResolveAsync(string adapter, string proxy)
        {
            // There is no corresponding service, we're just testing the endpoints.
            var greeter = IGreeterPrx.Parse(proxy, _pipeline);
            var indirectGreeter = IGreeterPrx.Parse($"{greeter.GetIdentity()} @ {adapter}", _pipeline);

            ISimpleLocatorTestPrx locator = CreateLocator();
            _pipeline.Use(Interceptors.Locator(locator));
            _pipeline.Use(next => new InlineInvoker(
                (request, cancel) =>
                {
                    if (request.Proxy == indirectGreeter)
                    {
                        Assert.AreEqual(greeter.Endpoint, request.Endpoint);
                        _called = true;
                    }
                    return next.InvokeAsync(request, cancel);
                }));
            _pipeline.Use(Interceptors.Binder(_pool));

            await locator.RegisterAdapterAsync(adapter, greeter);

            CollectionAssert.IsEmpty(indirectGreeter.AltEndpoints);
            Assert.AreEqual(Transport.Loc, indirectGreeter.Endpoint!.Transport);

            IServicePrx? found = await locator.FindAdapterByIdAsync(adapter);
            Assert.That(found, Is.Not.Null);
            Assert.AreEqual(found!.Endpoint, greeter.Endpoint);

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
            var indirectGreeter = IGreeterPrx.Parse($"{_greeter.GetIdentity()} @ adapt", _pipeline);
            var wellKnownGreeter = IGreeterPrx.Parse(_greeter.GetIdentity().ToString(), _pipeline);

            ISimpleLocatorTestPrx locator = CreateLocator();
            _pipeline.Use(Interceptors.CustomRetry(new Interceptors.RetryOptions() { MaxAttempts = 2}));
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
                    if (request.Proxy == indirectGreeter && request.Endpoint?.Transport != Transport.Loc)
                    {
                        Assert.AreEqual(_greeter.Endpoint, request.Endpoint);
                        _called = true;
                    }
                    else if (request.Proxy == wellKnownGreeter && request.Endpoint != null)
                    {
                        Assert.AreEqual(_greeter.Endpoint, request.Endpoint);
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
            await locator.RegisterWellKnownProxyAsync(_greeter.GetIdentity(), indirectGreeter);
            await locator.RegisterAdapterAsync("adapt", _greeter);
            _called = false;
            Assert.DoesNotThrowAsync(async () => await wellKnownGreeter.SayHelloAsync());
            Assert.That(_called, Is.True);

            Assert.That(await locator.UnregisterWellKnownProxyAsync(_greeter.GetIdentity()), Is.True);

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
            var greeter = IGreeterPrx.Parse(proxy, _pipeline);
            Identity identity = greeter.GetIdentity();

            var wellKnownGreeter = IGreeterPrx.Parse(identity.ToString(), _pipeline);
            Assert.That(wellKnownGreeter.Endpoint, Is.Null);

            ISimpleLocatorTestPrx locator = CreateLocator();
            _pipeline.Use(Interceptors.Locator(locator));
            _pipeline.Use(next => new InlineInvoker(
                (request, cancel) =>
                {
                    if (request.Proxy.Endpoint == null && request.GetIdentity() == identity)
                    {
                        Assert.AreEqual(greeter.Endpoint, request.Endpoint);
                        _called = true;
                    }
                    return next.InvokeAsync(request, cancel);
                }));
            _pipeline.Use(Interceptors.Binder(_pool));

            // Test with direct endpoints
            await locator.RegisterWellKnownProxyAsync(identity, greeter);
            IServicePrx? found = await locator.FindObjectByIdAsync(identity);
            Assert.That(found, Is.Not.Null);
            Assert.AreEqual(found!.Endpoint, greeter.Endpoint);

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
            var indirectGreeter = IGreeterPrx.Parse($"{identity} @ '{adapter}'", _pipeline);
            Assert.AreEqual($"loc -h {adapter} -p 0", indirectGreeter.Endpoint?.ToString());

            await locator.RegisterAdapterAsync(adapter, greeter);

            Assert.That(await locator.UnregisterWellKnownProxyAsync(identity), Is.True);
            await locator.RegisterWellKnownProxyAsync(identity, indirectGreeter);

            found = await locator.FindObjectByIdAsync(identity);
            Assert.That(found, Is.Not.Null);
            Assert.AreEqual(indirectGreeter.Endpoint, found!.Endpoint); // partial resolution

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
        public async Task TearDownAsync()
        {
            await _server.ShutdownAsync();
            await _pool.ShutdownAsync();
        }

        private ISimpleLocatorTestPrx CreateLocator()
        {
            string path = $"/{Guid.NewGuid()}";
            (_server.Dispatcher as Router)!.Map(path, new Locator());

            var locator = ISimpleLocatorTestPrx.FromServer(_server, path);
            locator.Invoker = _pipeline;
            return locator;
        }

        private class Locator : ISimpleLocatorTest
        {
            private readonly IDictionary<string, IServicePrx> _adapterMap =
                new ConcurrentDictionary<string, IServicePrx>();
            private readonly IDictionary<Identity, IServicePrx> _identityMap =
                new ConcurrentDictionary<Identity, IServicePrx>();

            public ValueTask<IServicePrx?> FindObjectByIdAsync(
                Identity id,
                Dispatch dispatch,
                CancellationToken cancel) =>
                new(_identityMap.TryGetValue(id, out IServicePrx? value) ? value : null);

            public ValueTask<IServicePrx?> FindAdapterByIdAsync(string id, Dispatch dispatch, CancellationToken cancel) =>
                new(_adapterMap.TryGetValue(id, out IServicePrx? value) ? value : null);

            public ValueTask<ILocatorRegistryPrx?> GetRegistryAsync(Dispatch dispatch, CancellationToken cancel)
            {
                Assert.Fail("unexpected call to GetRegistryAsync");
                return new(null as ILocatorRegistryPrx);
            }

            public ValueTask RegisterAdapterAsync(
                string adapter,
                IServicePrx dummy,
                Dispatch dispatch,
                CancellationToken cancel)
            {
                _adapterMap.Add(adapter, dummy);
                return default;
            }

            public ValueTask RegisterWellKnownProxyAsync(
                Identity identity,
                IServicePrx dummy,
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

        private class Greeter : IGreeter
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
