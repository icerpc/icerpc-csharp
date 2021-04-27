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
    [Parallelizable(ParallelScope.All)]
    [Timeout(30000)]
    public class InteropLocatorClientTests
    {
        private readonly Communicator _communicator;
        private IGreeterTestServicePrx _greeter;
        private Server _server;

        public InteropLocatorClientTests()
        {
            _communicator = new Communicator();
            var router = new Router();
            string path = $"/{System.Guid.NewGuid()}";
            router.Map(path, new GreeterTestService());
            _server = new Server
            {
                Communicator = _communicator,
                HasColocEndpoint = false,
                Dispatcher = router,
                Endpoint = "tcp -h 127.0.0.1 -p 0",
                ProxyHost = "localhost"
            };

            _server.Listen();

            // Must be created after Listen to get the port number.
            _greeter = _server.CreateProxy<IGreeterTestServicePrx>(path);
        }

        [TestCase("adapt1", "foo:tcp -h host1 -p 10000")]
        [TestCase("adapt2", "bar:ssl -h host1 -p 10000:udp -h host2 -p 20000")]
        [TestCase("adapt3", "xyz:wss -h host1 -p 10000 -z")]
        /// <summary>Verifies that LocatorClient.ResolveAsync works properly for "adapter" loc endpoints.</summary>
        public async Task InteropLocatorClient_AdapterResolveAsync(string adapter, string proxy)
        {
            ISimpleLocatorTestPrx locator = CreateLocator();
            ILocationResolver locationResolver = new LocatorClient(locator);

            // There is no corresponding service, we're just testing the endpoints.
            var greeter = IGreeterTestServicePrx.Parse(proxy, _communicator);
            await locator.RegisterAdapterAsync(adapter, greeter);

            var indirectGreeter = IGreeterTestServicePrx.Parse($"{greeter.GetIdentity()} @ {adapter}", _communicator);
            CollectionAssert.IsEmpty(indirectGreeter.AltEndpoints);
            Assert.That(indirectGreeter.Endpoint.StartsWith("loc ", StringComparison.Ordinal), Is.True);

            IServicePrx? found = await locator.FindAdapterByIdAsync(adapter);
            Assert.IsNotNull(found);
            Assert.AreEqual(found!.Endpoint, greeter.Endpoint);

            IReadOnlyList<Endpoint> endpoints =
                await locationResolver.ResolveAsync(Endpoint.Parse(indirectGreeter.Endpoint), refreshCache: false, default);

            Assert.AreEqual(endpoints[0].ToString(), greeter.Endpoint);
        }

        [TestCase(1)]
        [TestCase(2)]
        /// <summary>Makes sure a default-constructed locator client caches resolutions.</summary>
        public async Task InteropLocationClient_Cache(int cacheMaxSize)
        {
            ISimpleLocatorTestPrx locator = CreateLocator();
            ILocationResolver locationResolver = new LocatorClient(
                locator,
                new LocatorClientOptions { CacheMaxSize = cacheMaxSize, JustRefreshedAge = TimeSpan.Zero });

            var indirectGreeter = IGreeterTestServicePrx.Parse($"{_greeter.GetIdentity()} @ adapt", _communicator);

            // We don't cache the connection in order to use the location resolver (locator client) for each invocation.
            indirectGreeter.CacheConnection = false;
            indirectGreeter.LocationResolver = locationResolver;

            Assert.ThrowsAsync<NoEndpointException>(async () => await indirectGreeter.SayHelloAsync());
            await locator.RegisterAdapterAsync("adapt", _greeter);
            await indirectGreeter.SayHelloAsync();

            IReadOnlyList<Endpoint> endpoints =
                await locationResolver.ResolveAsync(Endpoint.Parse(indirectGreeter.Endpoint), refreshCache: false, default);
            Assert.AreEqual(endpoints[0].ToString(), _greeter.Endpoint);

            Assert.IsTrue(await locator.UnregisterAdapterAsync("adapt"));

            // We still find it in the cache and can still call it
            endpoints =
                await locationResolver.ResolveAsync(Endpoint.Parse(indirectGreeter.Endpoint), refreshCache: false, default);
            Assert.AreEqual(endpoints[0].ToString(), _greeter.Endpoint);
            await indirectGreeter.SayHelloAsync();

            // Force re-resolution (works because JustRefreshedAge is zero)
            endpoints =
                await locationResolver.ResolveAsync(Endpoint.Parse(indirectGreeter.Endpoint), refreshCache: true, default);
            CollectionAssert.IsEmpty(endpoints);
            Assert.ThrowsAsync<NoEndpointException>(async () => await indirectGreeter.SayHelloAsync());

            // Same with well-known greeter

            var wellKnownGreeter = IGreeterTestServicePrx.Parse(_greeter.GetIdentity().ToString(), _communicator);
            wellKnownGreeter.CacheConnection = false;
            wellKnownGreeter.LocationResolver = locationResolver;

            Assert.ThrowsAsync<NoEndpointException>(async () => await wellKnownGreeter.SayHelloAsync());
            await locator.RegisterWellKnownProxyAsync(_greeter.GetIdentity(), indirectGreeter);
            await locator.RegisterAdapterAsync("adapt", _greeter);
            await wellKnownGreeter.SayHelloAsync();

            endpoints =
                await locationResolver.ResolveAsync(Endpoint.Parse(wellKnownGreeter.Endpoint), refreshCache: false, default);

            Assert.AreEqual(endpoints[0].ToString(), _greeter.Endpoint);

            Assert.IsTrue(await locator.UnregisterWellKnownProxyAsync(_greeter.GetIdentity()));

            if (cacheMaxSize > 1)
            {
                // We still find it in the cache and can still call it.
                endpoints = await locationResolver.ResolveAsync(Endpoint.Parse(wellKnownGreeter.Endpoint),
                                                                 refreshCache: false,
                                                                 default);
                Assert.AreEqual(endpoints[0].ToString(), _greeter.Endpoint);
                await wellKnownGreeter.SayHelloAsync();

                // Force re-resolution
                endpoints =
                    await locationResolver.ResolveAsync(Endpoint.Parse(wellKnownGreeter.Endpoint), refreshCache: true, default);
                CollectionAssert.IsEmpty(endpoints);
            }
            Assert.ThrowsAsync<NoEndpointException>(async () => await wellKnownGreeter.SayHelloAsync());
        }

        [TestCase("foo:tcp -h host1 -p 10000")]
        [TestCase("bar:ssl -h host1 -p 10000:udp -h host2 -p 20000")]
        [TestCase("cat/xyz:wss -h host1 -p 10000 -z")]
        /// <summary>Verifies that LocatorClient.ResolveAsync works properly for well-known proxy loc endpoints.
        /// </summary>
        public async Task InteropLocatorClient_WellKnownProxyResolveAsync(string proxy)
        {
            ISimpleLocatorTestPrx locator = CreateLocator();
            ILocationResolver locationResolver = new LocatorClient(locator);

            // There is no corresponding service, we're just testing the endpoints.
            var greeter = IGreeterTestServicePrx.Parse(proxy, _communicator);
            Identity identity = greeter.GetIdentity();

            // Test with direct endpoints
            await locator.RegisterWellKnownProxyAsync(identity, greeter);

            var wellKnownGreeter = IGreeterTestServicePrx.Parse(identity.ToString(), _communicator);
            CollectionAssert.IsEmpty(wellKnownGreeter.AltEndpoints);
            Assert.AreEqual(Transport.Loc, Endpoint.Parse(wellKnownGreeter.Endpoint).Transport);

            IServicePrx? found = await locator.FindObjectByIdAsync(identity);
            Assert.IsNotNull(found);
            Assert.AreEqual(found!.Endpoint, greeter.Endpoint);

            IReadOnlyList<Endpoint> endpoints =
                await locationResolver.ResolveAsync(Endpoint.Parse(wellKnownGreeter.Endpoint), refreshCache: false, default);

            Assert.AreEqual(endpoints[0].ToString(), greeter.Endpoint);

            // Test with indirect endpoints
            string adapter = $"adapter/{identity.Category}/{identity.Name}";
            var indirectGreeter = IGreeterTestServicePrx.Parse($"{identity} @ '{adapter}'", _communicator);
            Assert.AreEqual($"loc -h {adapter} -p 0", indirectGreeter.Endpoint);

            await locator.RegisterAdapterAsync(adapter, greeter);

            Assert.IsTrue(await locator.UnregisterWellKnownProxyAsync(identity));
            await locator.RegisterWellKnownProxyAsync(identity, indirectGreeter);

            found = await locator.FindObjectByIdAsync(identity);
            Assert.IsNotNull(found);
            Assert.AreEqual(indirectGreeter.Endpoint, found!.Endpoint); // partial resolution

            endpoints =
                await locationResolver.ResolveAsync(Endpoint.Parse(wellKnownGreeter.Endpoint), refreshCache: false, default);

            Assert.AreEqual(endpoints[0].ToString(), greeter.Endpoint); // full resolution
        }

        [OneTimeTearDown]
        public async Task TearDownAsync()
        {
            await _server.ShutdownAsync();
            await _communicator.ShutdownAsync();
        }

        private ISimpleLocatorTestPrx CreateLocator()
        {
            string path = $"/{System.Guid.NewGuid()}";
            (_server.Dispatcher as Router)!.Map(path, new Locator());
            return _server.CreateProxy<ISimpleLocatorTestPrx>(path);
        }

        private class Locator : IAsyncSimpleLocatorTest
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

        private class GreeterTestService : IAsyncGreeterTestService
        {
            public ValueTask SayHelloAsync(Dispatch dispatch, CancellationToken cancel) => default;
        }
    }
}
