// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using NUnit.Framework;

namespace IceRpc.Tests.Internal
{
    [Parallelizable(ParallelScope.All)]
    public class LocatorEndpointFinderTests
    {
        [Test]
        public async Task LocatorEndpointFinder_SuccessNullAsync()
        {
            IEndpointFinder endpointFinder = new LocatorEndpointFinder(new FakeLocatorPrx());

            Proxy? proxy = await endpointFinder.FindAsync(new Location("good"), cancel: default);
            Assert.That(proxy, Is.Not.Null);
            Assert.That(proxy.Endpoint, Is.Not.Null);
            proxy = await endpointFinder.FindAsync(new Location("bad"), cancel: default);
            Assert.That(proxy, Is.Null);

            proxy = await endpointFinder.FindAsync(new Location(new Identity("good", "category")), cancel: default);
            Assert.That(proxy, Is.Not.Null);
            Assert.That(proxy.Endpoint, Is.Not.Null);
            proxy = await endpointFinder.FindAsync(new Location(new Identity("bad", "category")), cancel: default);
            Assert.That(proxy, Is.Null);
        }

        [Test]
        public void LocatorEndpointFinder_InvalidDataException()
        {
            IEndpointFinder endpointFinder = new LocatorEndpointFinder(new InvalidProxyLocatorPrx());

            Assert.ThrowsAsync<InvalidDataException>(async () =>
                await endpointFinder.FindAsync(new Location("loc"), cancel: default));

            Assert.ThrowsAsync<InvalidDataException>(async () =>
                await endpointFinder.FindAsync(new Location("foo"), cancel: default));

            Assert.ThrowsAsync<InvalidDataException>(async () =>
                await endpointFinder.FindAsync(new Location(new Identity("protocol", "category")), cancel: default));

            Assert.ThrowsAsync<InvalidDataException>(async () =>
                await endpointFinder.FindAsync(new Location(new Identity("name", "category")), cancel: default));
        }

        [Test]
        public async Task LocatorEndpointFinder_NotFoundExceptionAsync()
        {
            IEndpointFinder endpointFinder = new LocatorEndpointFinder(new ThrowingLocatorPrx());

            Assert.That(await endpointFinder.FindAsync(new Location("adapter"), cancel: default), Is.Null);
            Assert.That(
                await endpointFinder.FindAsync(new Location(new Identity("name", "category")), cancel: default),
                Is.Null);
        }

        [Test]
        public async Task CacheUpdateEndpointFinderDecorator_RemoveSetAsync()
        {
            var endpointCache = new FakeEndpointCache();

            IEndpointFinder endpointFinder = new CacheUpdateEndpointFinderDecorator(
                new FakeEndpointFinder(),
                endpointCache);

            Assert.That(endpointCache.Removed, Is.False);
            Assert.That(endpointCache.Set, Is.False);

            await endpointFinder.FindAsync(new Location("good"), cancel: default);

            Assert.That(endpointCache.Removed, Is.False);
            Assert.That(endpointCache.Set, Is.True);
            endpointCache.Set = false;

            await endpointFinder.FindAsync(new Location("bad"), cancel: default);
            Assert.That(endpointCache.Removed, Is.True);
            Assert.That(endpointCache.Set, Is.False);
        }

        [Test]
        public async Task CoalesceEndpointFinderDecorator_FindAsync()
        {
            using var blockingEndpointFinder = new BlockingEndpointFinder();

            IEndpointFinder endpointFinder = new CoalesceEndpointFinderDecorator(blockingEndpointFinder);

            var locA = new Location("a");
            var locB = new Location("b");

            Task<Proxy?> t1 = endpointFinder.FindAsync(locA, cancel: default);
            Task<Proxy?> t2 = endpointFinder.FindAsync(locA, cancel: default);
            Task<Proxy?> t3 = endpointFinder.FindAsync(locB, cancel: default);
            Task<Proxy?> t4 = endpointFinder.FindAsync(locA, cancel: default);
            Task<Proxy?> t5 = endpointFinder.FindAsync(locA, cancel: default);
            Task<Proxy?> t6 = endpointFinder.FindAsync(locB, cancel: default);

            Assert.AreEqual(0, blockingEndpointFinder.Count);
            blockingEndpointFinder.Release(2);
            await Task.WhenAll(t1, t2, t3, t4, t5, t6);
            Assert.AreEqual(2, blockingEndpointFinder.Count);
        }

        private class FakeLocatorPrx : ILocatorPrx
        {
            Task<ServicePrx?> ILocatorPrx.FindAdapterByIdAsync(
                string id,
                Invocation? invocation,
                CancellationToken cancel) =>
                    Task.FromResult<ServicePrx?>(id == "good" ? ServicePrx.Parse("dummy:tcp -h host -p 10000") : null);

            Task<ServicePrx?> ILocatorPrx.FindObjectByIdAsync(
                Identity id,
                Invocation? invocation,
                CancellationToken cancel) =>
                    Task.FromResult<ServicePrx?>(id.Name == "good" ? ServicePrx.Parse("dummy @ adapter") : null);

            Task<LocatorRegistryPrx?> ILocatorPrx.GetRegistryAsync(Invocation? invocation, CancellationToken cancel)
            {
                Assert.Fail("unexpected call to GetRegistryAsync");
                return Task.FromResult<LocatorRegistryPrx?>(null);
            }
        }

        private class InvalidProxyLocatorPrx : ILocatorPrx
        {
            Task<ServicePrx?> ILocatorPrx.FindAdapterByIdAsync(
                string id,
                Invocation? invocation,
                CancellationToken cancel) =>
                    Task.FromResult<ServicePrx?>(ServicePrx.Parse(id == "loc" ? "dummy @ adapter" : "dummy"));

            Task<ServicePrx?> ILocatorPrx.FindObjectByIdAsync(
                Identity id,
                Invocation? invocation,
                CancellationToken cancel) =>
                    Task.FromResult<ServicePrx?>(
                        ServicePrx.Parse(id.Name == "protocol" ? "ice+foo://host/dummy" : "dummy"));

            Task<LocatorRegistryPrx?> ILocatorPrx.GetRegistryAsync(Invocation? invocation, CancellationToken cancel)
            {
                Assert.Fail("unexpected call to GetRegistryAsync");
                return Task.FromResult<LocatorRegistryPrx?>(null);
            }
        }

        private class ThrowingLocatorPrx : ILocatorPrx
        {
            Task<ServicePrx?> ILocatorPrx.FindAdapterByIdAsync(
                string id,
                Invocation? invocation,
                CancellationToken cancel) =>
                throw new AdapterNotFoundException();

            Task<ServicePrx?> ILocatorPrx.FindObjectByIdAsync(
                Identity id,
                Invocation? invocation,
                CancellationToken cancel) => throw new ObjectNotFoundException();

            Task<LocatorRegistryPrx?> ILocatorPrx.GetRegistryAsync(Invocation? invocation, CancellationToken cancel)
            {
                Assert.Fail("unexpected call to GetRegistryAsync");
                return Task.FromResult<LocatorRegistryPrx?>(null);
            }
        }

        private class FakeEndpointCache : IEndpointCache
        {
            internal bool Removed { get; set; }
            internal bool Set { get; set; }

            void IEndpointCache.Remove(Location location) => Removed = true;
            void IEndpointCache.Set(Location location, Proxy proxy) => Set = true;
            bool IEndpointCache.TryGetValue(Location location, out (TimeSpan InsertionTime, Proxy Proxy) value)
            {
                Assert.Fail("unexpected call to TryGetValue");
                value = default;
                return false;
            }
        }

        private class FakeEndpointFinder : IEndpointFinder
        {
            Task<Proxy?> IEndpointFinder.FindAsync(Location location, CancellationToken cancel) =>
                Task.FromResult<Proxy?>(
                    location.AdapterId == "good" ? Proxy.Parse("dummy:tcp -h localhost -p 10000") : null);
        }

        private class BlockingEndpointFinder : IEndpointFinder, IDisposable
        {
            internal int Count;

            private readonly SemaphoreSlim _semaphore = new(0);

            internal void Release(int count) => _semaphore.Release(count);

            void IDisposable.Dispose() => _semaphore.Dispose();

            async Task<Proxy?> IEndpointFinder.FindAsync(Location location, CancellationToken cancel)
            {
                await _semaphore.WaitAsync(cancel);
                Interlocked.Increment(ref Count);

                return Proxy.Parse("dummy:tcp -h localhost -p 10000");
            }
        }
    }
}
