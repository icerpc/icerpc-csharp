// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Locator.Internal;
using IceRpc.Slice;
using NUnit.Framework;

namespace IceRpc.Locator.Tests;

public class LocatorEndpointFinderTests
{
    /// <summary>Verifies that <see cref="LocatorEndpointFinder"/> correctly resolves an adapter ID.</summary>
    [Test]
    public async Task Find_adapter_by_id()
    {
        var expectedServiceAddress = new ServiceProxy { ServiceAddress = new(new Uri("ice://localhost/dummy:10000")) };
        IEndpointFinder endpointFinder = new LocatorEndpointFinder(new FakeLocatorProxy(expectedServiceAddress, adapterId: true));
        var location = new Location { IsAdapterId = true, Value = "good" };

        ServiceAddress? serviceAddress = await endpointFinder.FindAsync(location, default);

        Assert.That(serviceAddress, Is.EqualTo(expectedServiceAddress.ServiceAddress));
    }

    /// <summary>Verifies that <see cref="LocatorEndpointFinder"/> correctly handles
    /// <see cref="AdapterNotFoundException"/>.</summary>
    [Test]
    public async Task Find_adapter_by_id_not_found()
    {
        IEndpointFinder endpointFinder = new LocatorEndpointFinder(new NotFoundLocatorProxy());
        var location = new Location { IsAdapterId = true, Value = "good" };

        ServiceAddress? serviceAddress = await endpointFinder.FindAsync(location, default);

        Assert.That(serviceAddress, Is.Null);
    }

    /// <summary>Verifies that <see cref="LocatorEndpointFinder"/> correctly resolves an object ID.</summary>
    [Test]
    public void Find_adapter_by_id_returning_a_proxy_without_endpoint_fails()
    {
        IEndpointFinder endpointFinder = new LocatorEndpointFinder(
            new FakeLocatorProxy(new ServiceProxy { ServiceAddress = new(new Uri("ice:/dummy")) }, adapterId: true));
        var location = new Location { IsAdapterId = true, Value = "good" };

        Assert.That(
            async () => await endpointFinder.FindAsync(location, default),
            Throws.TypeOf<InvalidDataException>());
    }

    /// <summary>Verifies that <see cref="LocatorEndpointFinder"/> correctly resolves an object ID.</summary>
    [Test]
    public async Task Find_object_by_id()
    {
        var expectedServiceAddress = new ServiceProxy { ServiceAddress = new(new Uri("ice://localhost/dummy:10000")) };
        IEndpointFinder endpointFinder = new LocatorEndpointFinder(new FakeLocatorProxy(expectedServiceAddress, adapterId: false));
        var location = new Location { IsAdapterId = false, Value = "good" };

        ServiceAddress? serviceAddress = await endpointFinder.FindAsync(location, default);

        Assert.That(serviceAddress, Is.EqualTo(expectedServiceAddress.ServiceAddress));
    }

    /// <summary>Verifies that <see cref="LocatorEndpointFinder"/> correctly handles
    /// <see cref="ObjectNotFoundException"/>.</summary>
    [Test]
    public async Task Find_object_by_id_not_found()
    {
        IEndpointFinder endpointFinder = new LocatorEndpointFinder(new NotFoundLocatorProxy());
        var location = new Location { IsAdapterId = false, Value = "good" };

        ServiceAddress? serviceAddress = await endpointFinder.FindAsync(location, default);

        Assert.That(serviceAddress, Is.Null);
    }

    /// <summary>Verifies that <see cref="LocatorEndpointFinder"/> correctly resolves an object ID.</summary>
    [Test]
    public void Find_object_by_id_returning_proxy_without_endpoint_fails()
    {
        IEndpointFinder endpointFinder = new LocatorEndpointFinder(
            new FakeLocatorProxy(new ServiceProxy { ServiceAddress = new(new Uri("ice:/dummy")) }, adapterId: false));
        var location = new Location { IsAdapterId = false, Value = "good" };

        Assert.That(
            async () => await endpointFinder.FindAsync(location, default),
            Throws.TypeOf<InvalidDataException>());
    }

    /// <summary>Verifies that <see cref="LocatorEndpointFinder"/> correctly resolves an object ID.</summary>
    [Test]
    public void Find_object_by_id_returning_proxy_without_ice_protocol_fails()
    {
        IEndpointFinder endpointFinder = new LocatorEndpointFinder(
            new FakeLocatorProxy(new ServiceProxy { ServiceAddress = new(new Uri("icerpc://localhost/dummy:10000")) }, adapterId: false));
        var location = new Location { IsAdapterId = false, Value = "good" };

        Assert.That(
            async () => await endpointFinder.FindAsync(location, default),
            Throws.TypeOf<InvalidDataException>());
    }

    /// <summary>Verifies that <see cref="CacheUpdateEndpointFinderDecorator"/> adds found entries
    /// to the endpoint cache.</summary>
    [Test]
    public async Task Cache_decorator_adds_found_entries_to_the_endpoint_cache()
    {
        var endpointCache = new EndpointCache();
        var location = new Location { IsAdapterId = false, Value = "good" };
        var expectedServiceAddress = new ServiceAddress(new Uri("ice://localhost/dummy:10000"));
        IEndpointFinder endpointFinder = new CacheUpdateEndpointFinderDecorator(
            new LocatorEndpointFinder(
                new FakeLocatorProxy(new ServiceProxy { ServiceAddress = expectedServiceAddress }, adapterId: false)),
            endpointCache);

        _ = await endpointFinder.FindAsync(location, default);

        Assert.That(endpointCache.Cache.Count, Is.EqualTo(1));
        Assert.That(endpointCache.Cache.ContainsKey(location), Is.True);
        Assert.That(endpointCache.Cache[location], Is.EqualTo(expectedServiceAddress));
    }

    /// <summary>Verifies that <see cref="CacheUpdateEndpointFinderDecorator"/> removes not found entries
    /// from the endpoint cache.</summary>
    [Test]
    public async Task Cache_decorator_removes_not_found_entries_from_the_endpoint_cache()
    {
        var endpointCache = new EndpointCache();
        var location = new Location { IsAdapterId = false, Value = "good" };
        var expectedServiceAddress = new ServiceAddress(new Uri("ice://localhost/dummy:10000"));
        endpointCache.Cache[location] = expectedServiceAddress;

        IEndpointFinder endpointFinder = new CacheUpdateEndpointFinderDecorator(
            new LocatorEndpointFinder(new NotFoundLocatorProxy()),
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
        var location = new Location { IsAdapterId = false, Value = "good" };

        var t1 = endpointFinder.FindAsync(location, default);
        var t2 = endpointFinder.FindAsync(location, default);
        var t3 = endpointFinder.FindAsync(location, default);

        // Act
        blockingEndpointFinder.Release(1);
        ServiceAddress? p1 = await t1;
        ServiceAddress? p2 = await t2;
        ServiceAddress? p3 = await t3;

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

        async Task<ServiceAddress?> IEndpointFinder.FindAsync(Location location, CancellationToken cancel)
        {
            await _semaphore.WaitAsync(cancel);
            Interlocked.Increment(ref Count);

            return new ServiceAddress(new Uri("dummy://localhost:10000"));
        }
    }

    private class EndpointCache : IEndpointCache
    {
        public Dictionary<Location, ServiceAddress> Cache { get; } = new();

        public void Remove(Location location) => Cache.Remove(location);
        public void Set(Location location, ServiceAddress serviceAddress) => Cache[location] = serviceAddress;
        public bool TryGetValue(Location location, out (TimeSpan InsertionTime, ServiceAddress ServiceAddress) value) =>
            throw new NotImplementedException();
    }

    private class FakeLocatorProxy : ILocatorProxy
    {
        private readonly ServiceProxy _service;
        private bool _adapterId;

        public FakeLocatorProxy(ServiceProxy service, bool adapterId)
        {
            _service = service;
            _adapterId = adapterId;
        }

        public Task<ServiceProxy?> FindAdapterByIdAsync(string id, IFeatureCollection? features, CancellationToken cancel) =>
            Task.FromResult<ServiceProxy?>(id == "good" && _adapterId ? _service : null);

        public Task<ServiceProxy?> FindObjectByIdAsync(string id, IFeatureCollection? features, CancellationToken cancel) =>
            Task.FromResult<ServiceProxy?>(id == "good" && !_adapterId ? _service : null);

        Task<LocatorRegistryProxy?> ILocatorProxy.GetRegistryAsync(IFeatureCollection? features, CancellationToken cancel) =>
            throw new NotImplementedException();
    }

    private class NotFoundLocatorProxy : ILocatorProxy
    {
        public Task<ServiceProxy?> FindAdapterByIdAsync(string id, IFeatureCollection? features, CancellationToken cancel) =>
            throw new AdapterNotFoundException();

        public Task<ServiceProxy?> FindObjectByIdAsync(string id, IFeatureCollection? features, CancellationToken cancel) =>
            throw new ObjectNotFoundException();

        public Task<LocatorRegistryProxy?> GetRegistryAsync(IFeatureCollection? features, CancellationToken cancel) =>
            throw new NotImplementedException();
    }
}
