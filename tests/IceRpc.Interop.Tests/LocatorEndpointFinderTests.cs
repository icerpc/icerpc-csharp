// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Internal;
using NUnit.Framework;

namespace IceRpc.Tests;

public class LocatorEndpointFinderTests
{
    /// <summary>Verifies <see cref="LocatorEndpointFinder"/> correctly resolves an adapter Id.</summary>
    [Test]
    public async Task Find_adapter_by_id()
    {
        var expectedProxy = ServicePrx.Parse("ice://localhost/dummy:10000");
        IEndpointFinder endpointFinder = new LocatorEndpointFinder(new FakeLocatorPrx(expectedProxy, adapterId: true));
        var location = new Location() { IsAdapterId = true, Value = "good" };

        Proxy? proxy = await endpointFinder.FindAsync(location, default);

        Assert.That(proxy, Is.EqualTo(expectedProxy.Proxy));
    }

    /// <summary>Verifies <see cref="LocatorEndpointFinder"/> correctly handles <see cref="AdapterNotFoundException"/>.
    /// </summary>
    [Test]
    public async Task Find_adapter_by_id_not_found()
    {
        IEndpointFinder endpointFinder = new LocatorEndpointFinder(new NotFoundLocatorPrx());
        var location = new Location() { IsAdapterId = true, Value = "good" };

        Proxy? proxy = await endpointFinder.FindAsync(location, default);

        Assert.That(proxy, Is.Null);
    }

    /// <summary>Verifies <see cref="LocatorEndpointFinder"/> correctly resolves an object Id.</summary>
    [Test]
    public async Task Find_object_by_id()
    {
        var expectedProxy = ServicePrx.Parse("ice://localhost/dummy:10000");
        IEndpointFinder endpointFinder = new LocatorEndpointFinder(new FakeLocatorPrx(expectedProxy, adapterId: false));
        var location = new Location() { IsAdapterId = false, Value = "good" };

        Proxy? proxy = await endpointFinder.FindAsync(location, default);

        Assert.That(proxy, Is.EqualTo(expectedProxy.Proxy));
    }

    /// <summary>Verifies <see cref="LocatorEndpointFinder"/> correctly handles <see cref="ObjectNotFoundException"/>.
    /// </summary>
    [Test]
    public async Task Find_object_by_id_not_found()
    {
        IEndpointFinder endpointFinder = new LocatorEndpointFinder(new NotFoundLocatorPrx());
        var location = new Location() { IsAdapterId = false, Value = "good" };

        Proxy? proxy = await endpointFinder.FindAsync(location, default);

        Assert.That(proxy, Is.Null);
    }

    /// <summary>Verifies that <see cref="CacheUpdateEndpointFinderDecorator"/> adds found entries
    /// to the endpoint cache.</summary>
    [Test]
    public async Task Cache_decorator_adds_found_entries_to_the_endpoint_cache()
    {
        var endpointCache = new EndpointCache();
        var location = new Location() { IsAdapterId = false, Value = "good" };
        var expectedProxy = Proxy.Parse("ice://localhost/dummy:10000");
        IEndpointFinder endpointFinder = new CacheUpdateEndpointFinderDecorator(
            new LocatorEndpointFinder(new FakeLocatorPrx(new ServicePrx(expectedProxy), adapterId: false)),
            endpointCache);

        _ = await endpointFinder.FindAsync(location, default);

        Assert.That(endpointCache.Cache.Count, Is.EqualTo(1));
        Assert.That(endpointCache.Cache.ContainsKey(location), Is.True);
        Assert.That(endpointCache.Cache[location], Is.EqualTo(expectedProxy));
    }

    /// <summary>Verifies that <see cref="CacheUpdateEndpointFinderDecorator"/> removes not found entries
    /// from the endpoint cache.</summary>
    [Test]
    public async Task Cache_decorator_removes_not_found_entries_from_the_endpoint_cache()
    {
        var endpointCache = new EndpointCache();
        var location = new Location() { IsAdapterId = false, Value = "good" };
        var expectedProxy = Proxy.Parse("ice://localhost/dummy:10000");
        endpointCache.Cache[location] = expectedProxy;

        IEndpointFinder endpointFinder = new CacheUpdateEndpointFinderDecorator(
            new LocatorEndpointFinder(new NotFoundLocatorPrx()),
            endpointCache);

        _ = await endpointFinder.FindAsync(location, default);

        Assert.That(endpointCache.Cache, Is.Empty);
    }

    /// <summary>Verifies that <see cref="CoalesceEndpointFinderDecorator"/> coalesce identical requests.</summary>
    [Test]
    public async Task Coalesce_decorator_coalesce_identical_requests()
    {
        // Arrange
        using var blockingEndpointFinder = new BlockingEndpointFinder();
        IEndpointFinder endpointFinder = new CoalesceEndpointFinderDecorator(blockingEndpointFinder);
        var location = new Location() { IsAdapterId = false, Value = "good" };

        var t1 = endpointFinder.FindAsync(location, default);
        var t2 = endpointFinder.FindAsync(location, default);
        var t3 = endpointFinder.FindAsync(location, default);

        // Act
        blockingEndpointFinder.Release(1);
        Proxy? p1 = await t1;
        Proxy? p2 = await t2;
        Proxy? p3 = await t3;

        // Assert
        Assert.That(blockingEndpointFinder.Count, Is.EqualTo(1));
        Assert.That(p1, Is.EqualTo(p2));
        Assert.That(p1, Is.EqualTo(p3));
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

            return Proxy.Parse("dummy:tcp -h localhost -p 10000", format: IceProxyFormat.Default);
        }
    }

    private class EndpointCache : IEndpointCache
    {
        public Dictionary<Location, Proxy> Cache { get; } = new();

        public void Remove(Location location) => Cache.Remove(location);
        public void Set(Location location, Proxy proxy) => Cache[location] = proxy;
        public bool TryGetValue(Location location, out (TimeSpan InsertionTime, Proxy Proxy) value) =>
            throw new NotImplementedException();
    }

    private class FakeLocatorPrx : ILocatorPrx
    {
        private readonly ServicePrx _service;
        private bool _adapterId;

        public FakeLocatorPrx(ServicePrx service, bool adapterId)
        {
            _service = service;
            _adapterId = adapterId;
        }

        public Task<ServicePrx?> FindAdapterByIdAsync(string id, Invocation? invocation, CancellationToken cancel) =>
            Task.FromResult<ServicePrx?>(id == "good" && _adapterId ? _service : null);

        public Task<ServicePrx?> FindObjectByIdAsync(string id, Invocation? invocation, CancellationToken cancel) =>
            Task.FromResult<ServicePrx?>(id == "good" && !_adapterId ? _service : null);

        Task<LocatorRegistryPrx?> ILocatorPrx.GetRegistryAsync(Invocation? invocation, CancellationToken cancel) =>
            throw new NotImplementedException();
    }

    private class NotFoundLocatorPrx : ILocatorPrx
    {
        public Task<ServicePrx?> FindAdapterByIdAsync(string id, Invocation? invocation, CancellationToken cancel) =>
            throw new AdapterNotFoundException();

        public Task<ServicePrx?> FindObjectByIdAsync(string id, Invocation? invocation, CancellationToken cancel) =>
            throw new ObjectNotFoundException();

        public Task<LocatorRegistryPrx?> GetRegistryAsync(Invocation? invocation, CancellationToken cancel) =>
            throw new NotImplementedException();
    }
}
