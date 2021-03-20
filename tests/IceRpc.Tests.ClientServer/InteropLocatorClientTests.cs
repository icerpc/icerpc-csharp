// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Interop;
using NUnit.Framework;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.ClientServer
{
    [Parallelizable(ParallelScope.All)]
    [Timeout(30000)]
    public class InteropLocatorClientTests : ClientServerBaseTest
    {
        private readonly Communicator _communicator;
        private IGreeterTestServicePrx _greeter;
        private Server _server;

        public InteropLocatorClientTests()
        {
            _communicator = new Communicator();
            _server = new Server(_communicator,
                                 new ServerOptions()
                                 {
                                    ColocationScope = ColocationScope.None,
                                    Endpoints = "tcp -h 127.0.0.1 -p 0"
                                 });

            _greeter = _server.AddWithUUID(new GreeterTestService(), IGreeterTestServicePrx.Factory);
            _server.Activate();
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
            Assert.AreEqual(indirectGreeter.Endpoints.Count, 1);
            Assert.AreEqual(indirectGreeter.Endpoints[0].Transport, Transport.Loc);

            IServicePrx? found = await locator.FindAdapterByIdAsync(adapter);
            Assert.IsNotNull(found);
            Assert.IsTrue(found!.Endpoints.SequenceEqual(greeter.Endpoints));

            (IReadOnlyList<Endpoint> endpoints, _) =
                await locationResolver.ResolveAsync(indirectGreeter.Endpoints[0], Timeout.InfiniteTimeSpan, default);

            Assert.IsTrue(endpoints.SequenceEqual(greeter.Endpoints));;
        }

        [TestCase]
        /// <summary>Makes sure a default-constructed locator client caches resolutions.</summary>
        public async Task InteropLocationClient_Cache()
        {
            ISimpleLocatorTestPrx locator = CreateLocator();
            ILocationResolver locationResolver = new LocatorClient(locator);

            var indirectGreeter = IGreeterTestServicePrx.Parse($"{_greeter.GetIdentity()} @ adapt", _communicator);

            // We don't cache the connection in order to use the location resolver (locator client) for each invocation.
            indirectGreeter = indirectGreeter.Clone(cacheConnection: false, locationResolver: locationResolver);

            Assert.ThrowsAsync<NoEndpointException>(async () => await indirectGreeter.SayHelloAsync());
            await locator.RegisterAdapterAsync("adapt", _greeter);
            await indirectGreeter.SayHelloAsync();

            (IReadOnlyList<Endpoint> endpoints, TimeSpan age) =
                await locationResolver.ResolveAsync(indirectGreeter.Endpoints[0], Timeout.InfiniteTimeSpan, default);
            Assert.IsTrue(age > TimeSpan.Zero);
            Assert.IsTrue(endpoints.SequenceEqual(_greeter.Endpoints));
            Assert.IsTrue(await locator.UnregisterAdapterAsync("adapt"));

            // We still find it in the cache and can still call it
            (endpoints, age) =
                await locationResolver.ResolveAsync(indirectGreeter.Endpoints[0], Timeout.InfiniteTimeSpan, default);
            Assert.IsTrue(endpoints.SequenceEqual(_greeter.Endpoints));
            Assert.IsTrue(age > TimeSpan.Zero);
            await indirectGreeter.SayHelloAsync();

            // Force re-resolution
            (endpoints, age) =
                await locationResolver.ResolveAsync(indirectGreeter.Endpoints[0], TimeSpan.Zero, default);
            Assert.AreEqual(endpoints.Count, 0);
            Assert.AreEqual(age, TimeSpan.Zero);
            Assert.ThrowsAsync<NoEndpointException>(async () => await indirectGreeter.SayHelloAsync());

            // Same with well-known greeter

            var wellKnownGreeter = IGreeterTestServicePrx.Parse(_greeter.GetIdentity().ToString(), _communicator);
            wellKnownGreeter = wellKnownGreeter.Clone(cacheConnection: false, locationResolver: locationResolver);

            Assert.ThrowsAsync<NoEndpointException>(async () => await wellKnownGreeter.SayHelloAsync());
            await locator.RegisterWellKnownProxyAsync(_greeter.GetIdentity(), _greeter);
            await wellKnownGreeter.SayHelloAsync();

            (endpoints, age) =
                await locationResolver.ResolveAsync(wellKnownGreeter.Endpoints[0], Timeout.InfiniteTimeSpan, default);
            Assert.IsTrue(age > TimeSpan.Zero);
            Assert.IsTrue(endpoints.SequenceEqual(_greeter.Endpoints));
            Assert.IsTrue(await locator.UnregisterWellKnownProxyAsync(_greeter.GetIdentity()));

            // We still find it in the cache and can still call it.
            (endpoints, age) =
                await locationResolver.ResolveAsync(wellKnownGreeter.Endpoints[0], Timeout.InfiniteTimeSpan, default);
            Assert.IsTrue(endpoints.SequenceEqual(_greeter.Endpoints));
            Assert.IsTrue(age > TimeSpan.Zero);
            await wellKnownGreeter.SayHelloAsync();

            // Force re-resolution
            (endpoints, age) =
                await locationResolver.ResolveAsync(wellKnownGreeter.Endpoints[0], TimeSpan.Zero, default);
            Assert.AreEqual(endpoints.Count, 0);
            Assert.AreEqual(age, TimeSpan.Zero);
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
            ILocationResolver locationResolver  = new LocatorClient(locator);

            // There is no corresponding service, we're just testing the endpoints.
            var greeter = IGreeterTestServicePrx.Parse(proxy, _communicator);
            Identity identity = greeter.GetIdentity();

            // Test with direct endpoints
            await locator.RegisterWellKnownProxyAsync(identity, greeter);

            var wellKnownGreeter = IGreeterTestServicePrx.Parse(identity.ToString(), _communicator);
            Assert.AreEqual(wellKnownGreeter.Endpoints.Count, 1);
            Assert.AreEqual(wellKnownGreeter.Endpoints[0].Transport, Transport.Loc);

            IServicePrx? found = await locator.FindObjectByIdAsync(identity);
            Assert.IsNotNull(found);
            Assert.IsTrue(found!.Endpoints.SequenceEqual(greeter.Endpoints));

            (IReadOnlyList<Endpoint> endpoints, _) =
                await locationResolver.ResolveAsync(wellKnownGreeter.Endpoints[0], Timeout.InfiniteTimeSpan, default);

            Assert.IsTrue(endpoints.SequenceEqual(greeter.Endpoints));

            // Test with indirect endpoints
            string adapter = $"adapter/{identity.Category}/{identity.Name}";
            var indirectGreeter = IGreeterTestServicePrx.Parse($"{identity} @ '{adapter}'", _communicator);
            await locator.RegisterAdapterAsync(adapter, greeter);

            Assert.IsTrue(await locator.UnregisterWellKnownProxyAsync(identity));
            await locator.RegisterWellKnownProxyAsync(identity, indirectGreeter);

            found = await locator.FindObjectByIdAsync(identity);
            Assert.IsNotNull(found);
            Assert.IsTrue(found!.Endpoints.SequenceEqual(indirectGreeter.Endpoints)); // partial resolution

            (endpoints, _) =
                await locationResolver.ResolveAsync(wellKnownGreeter.Endpoints[0], Timeout.InfiniteTimeSpan, default);

            Assert.IsTrue(endpoints.SequenceEqual(greeter.Endpoints)); // full resolution
        }

        [OneTimeTearDown]
        public async Task TearDownAsync()
        {
            await _server.ShutdownAsync();
            await _communicator.ShutdownAsync();
        }

        private ISimpleLocatorTestPrx CreateLocator() =>
            _server.AddWithUUID(new Locator(), ISimpleLocatorTestPrx.Factory);

        private class Locator : IAsyncSimpleLocatorTest
        {
            private IDictionary<string, IServicePrx> _adapterMap = new ConcurrentDictionary<string, IServicePrx>();
            private IDictionary<Identity, IServicePrx> _identityMap = new ConcurrentDictionary<Identity, IServicePrx>();

            public ValueTask<IServicePrx?> FindObjectByIdAsync(
                Identity id,
                Current current,
                CancellationToken cancel) =>
                new(_identityMap.TryGetValue(id, out IServicePrx? value) ? value : null);

            public ValueTask<IServicePrx?> FindAdapterByIdAsync(string id, Current current, CancellationToken cancel) =>
                new(_adapterMap.TryGetValue(id, out IServicePrx? value) ? value : null);

            public ValueTask<ILocatorRegistryPrx?> GetRegistryAsync(Current current, CancellationToken cancel)
            {
                Assert.Fail("unexpected call to GetRegistryAsync");
                return new(null as ILocatorRegistryPrx);
            }

            public ValueTask RegisterAdapterAsync(
                string adapter,
                IServicePrx dummy,
                Current current,
                CancellationToken cancel)
            {
                _adapterMap.Add(adapter, dummy);
                return default;
            }

            public ValueTask RegisterWellKnownProxyAsync(
                Identity identity,
                IServicePrx dummy,
                Current current,
                CancellationToken cancel)
            {
                _identityMap.Add(identity, dummy);
                return default;
            }

            public ValueTask<bool> UnregisterAdapterAsync(string adapter, Current current, CancellationToken cancel) =>
                new(_adapterMap.Remove(adapter));

            public ValueTask<bool> UnregisterWellKnownProxyAsync(
                Identity identity,
                Current current,
                CancellationToken cancel) =>
                new(_identityMap.Remove(identity));
        }

        private class GreeterTestService : IAsyncGreeterTestService
        {
            public ValueTask SayHelloAsync(Current current, CancellationToken cancel) => default;
        }
    }
}
