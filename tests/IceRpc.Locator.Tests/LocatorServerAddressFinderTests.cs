// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Locator.Internal;
using IceRpc.Slice;
using NUnit.Framework;

namespace IceRpc.Locator.Tests;

public class LocatorServerAddressFinderTests
{
    /// <summary>Verifies that <see cref="LocatorServerAddressFinder" /> correctly resolves an adapter ID.</summary>
    [Test]
    public async Task Find_adapter_by_id()
    {
        var expectedServiceAddress = new ServiceAddress(new Uri("ice://localhost/dummy:10000"));
        IServerAddressFinder serverAddressFinder = new LocatorServerAddressFinder(new FakeLocatorProxy(expectedServiceAddress, adapterId: true));
        var location = new Location { IsAdapterId = true, Value = "good" };

        ServiceAddress? serviceAddress = await serverAddressFinder.FindAsync(location, default);

        Assert.That(serviceAddress, Is.EqualTo(expectedServiceAddress));
    }

    /// <summary>Verifies that <see cref="LocatorServerAddressFinder" /> correctly handles
    /// <see cref="AdapterNotFoundException" />.</summary>
    [Test]
    public async Task Find_adapter_by_id_not_found()
    {
        IServerAddressFinder serverAddressFinder = new LocatorServerAddressFinder(new NotFoundLocatorProxy());
        var location = new Location { IsAdapterId = true, Value = "good" };

        ServiceAddress? serviceAddress = await serverAddressFinder.FindAsync(location, default);

        Assert.That(serviceAddress, Is.Null);
    }

    /// <summary>Verifies that <see cref="LocatorServerAddressFinder" /> correctly resolves an object ID.</summary>
    [Test]
    public void Find_adapter_by_id_returning_a_proxy_without_server_address_fails()
    {
        IServerAddressFinder serverAddressFinder = new LocatorServerAddressFinder(
            new FakeLocatorProxy(new ServiceAddress(new Uri("ice:/dummy")), adapterId: true));
        var location = new Location { IsAdapterId = true, Value = "good" };

        Assert.That(
            async () => await serverAddressFinder.FindAsync(location, default),
            Throws.TypeOf<InvalidDataException>());
    }

    /// <summary>Verifies that <see cref="LocatorServerAddressFinder" /> correctly resolves an object ID.</summary>
    [Test]
    public async Task Find_object_by_id()
    {
        var expectedServiceAddress = new ServiceAddress(new Uri("ice://localhost/dummy:10000"));
        IServerAddressFinder serverAddressFinder = new LocatorServerAddressFinder(new FakeLocatorProxy(expectedServiceAddress, adapterId: false));
        var location = new Location { IsAdapterId = false, Value = "good" };

        ServiceAddress? serviceAddress = await serverAddressFinder.FindAsync(location, default);

        Assert.That(serviceAddress, Is.EqualTo(expectedServiceAddress));
    }

    /// <summary>Verifies that <see cref="LocatorServerAddressFinder" /> correctly handles
    /// <see cref="ObjectNotFoundException" />.</summary>
    [Test]
    public async Task Find_object_by_id_not_found()
    {
        IServerAddressFinder serverAddressFinder = new LocatorServerAddressFinder(new NotFoundLocatorProxy());
        var location = new Location { IsAdapterId = false, Value = "good" };

        ServiceAddress? serviceAddress = await serverAddressFinder.FindAsync(location, default);

        Assert.That(serviceAddress, Is.Null);
    }

    /// <summary>Verifies that <see cref="LocatorServerAddressFinder" /> correctly resolves an object ID.</summary>
    [Test]
    public void Find_object_by_id_returning_proxy_without_server_address_fails()
    {
        IServerAddressFinder serverAddressFinder = new LocatorServerAddressFinder(
            new FakeLocatorProxy(new ServiceAddress(new Uri("ice:/dummy")), adapterId: false));
        var location = new Location { IsAdapterId = false, Value = "good" };

        Assert.That(
            async () => await serverAddressFinder.FindAsync(location, default),
            Throws.TypeOf<InvalidDataException>());
    }

    /// <summary>Verifies that <see cref="LocatorServerAddressFinder" /> correctly resolves an object ID.</summary>
    [Test]
    public void Find_object_by_id_returning_proxy_without_ice_protocol_fails()
    {
        IServerAddressFinder serverAddressFinder = new LocatorServerAddressFinder(
            new FakeLocatorProxy(new ServiceAddress(new Uri("icerpc://localhost/dummy:10000")), adapterId: false));
        var location = new Location { IsAdapterId = false, Value = "good" };

        Assert.That(
            async () => await serverAddressFinder.FindAsync(location, default),
            Throws.TypeOf<InvalidDataException>());
    }

    /// <summary>Verifies that <see cref="CacheUpdateServerAddressFinderDecorator" /> adds found entries
    /// to the server address cache.</summary>
    [Test]
    public async Task Cache_decorator_adds_found_entries_to_the_server_address_cache()
    {
        var serverAddressCache = new ServerAddressCache();
        var location = new Location { IsAdapterId = false, Value = "good" };
        var expectedServiceAddress = new ServiceAddress(new Uri("ice://localhost/dummy:10000"));
        IServerAddressFinder serverAddressFinder = new CacheUpdateServerAddressFinderDecorator(
            new LocatorServerAddressFinder(new FakeLocatorProxy(expectedServiceAddress, adapterId: false)),
            serverAddressCache);

        _ = await serverAddressFinder.FindAsync(location, default);

        Assert.That(serverAddressCache.Cache, Has.Count.EqualTo(1));
        Assert.That(serverAddressCache.Cache.ContainsKey(location), Is.True);
        Assert.That(serverAddressCache.Cache[location], Is.EqualTo(expectedServiceAddress));
    }

    /// <summary>Verifies that <see cref="CacheUpdateServerAddressFinderDecorator" /> removes not found entries
    /// from the server address cache.</summary>
    [Test]
    public async Task Cache_decorator_removes_not_found_entries_from_the_server_address_cache()
    {
        var serverAddressCache = new ServerAddressCache();
        var location = new Location { IsAdapterId = false, Value = "good" };
        var expectedServiceAddress = new ServiceAddress(new Uri("ice://localhost/dummy:10000"));
        serverAddressCache.Cache[location] = expectedServiceAddress;

        IServerAddressFinder serverAddressFinder = new CacheUpdateServerAddressFinderDecorator(
            new LocatorServerAddressFinder(new NotFoundLocatorProxy()),
            serverAddressCache);

        _ = await serverAddressFinder.FindAsync(location, default);

        Assert.That(serverAddressCache.Cache, Is.Empty);
    }

    /// <summary>Verifies that <see cref="CoalesceServerAddressFinderDecorator" /> coalesce identical requests.
    /// </summary>
    [Test]
    public async Task Coalesce_decorator_coalesce_identical_requests()
    {
        // Arrange
        using var blockingServerAddressFinder = new BlockingServerAddressFinder();
        IServerAddressFinder serverAddressFinder = new CoalesceServerAddressFinderDecorator(blockingServerAddressFinder);
        var location = new Location { IsAdapterId = false, Value = "good" };

        var t1 = serverAddressFinder.FindAsync(location, default);
        var t2 = serverAddressFinder.FindAsync(location, default);
        var t3 = serverAddressFinder.FindAsync(location, default);

        // Act
        blockingServerAddressFinder.Release(1);
        ServiceAddress? p1 = await t1;
        ServiceAddress? p2 = await t2;
        ServiceAddress? p3 = await t3;

        // Assert
        Assert.That(blockingServerAddressFinder.Count, Is.EqualTo(1));
        Assert.That(p1, Is.EqualTo(p2));
        Assert.That(p1, Is.EqualTo(p3));
    }

    private sealed class BlockingServerAddressFinder : IServerAddressFinder, IDisposable
    {
        internal int Count;

        private readonly SemaphoreSlim _semaphore = new(0);

        internal void Release(int count) => _semaphore.Release(count);

        void IDisposable.Dispose() => _semaphore.Dispose();

        async Task<ServiceAddress?> IServerAddressFinder.FindAsync(Location location, CancellationToken cancellationToken)
        {
            await _semaphore.WaitAsync(cancellationToken);
            Interlocked.Increment(ref Count);

            return new ServiceAddress(new Uri("ice://localhost:10000/dummy?transport=unknown"));
        }
    }

    private sealed class ServerAddressCache : IServerAddressCache
    {
        public Dictionary<Location, ServiceAddress> Cache { get; } = new();

        public void Remove(Location location) => Cache.Remove(location);
        public void Set(Location location, ServiceAddress serviceAddress) => Cache[location] = serviceAddress;
        public bool TryGetValue(Location location, out (TimeSpan InsertionTime, ServiceAddress ServiceAddress) value) =>
            throw new NotImplementedException();
    }

    private sealed class FakeLocatorProxy : ILocatorProxy
    {
        private readonly ServiceAddress _serviceAddress;
        private readonly bool _adapterId;

        public FakeLocatorProxy(ServiceAddress serviceAddress, bool adapterId)
        {
            _serviceAddress = serviceAddress;
            _adapterId = adapterId;
        }

        public Task<ServiceAddress?> FindAdapterByIdAsync(string id, IFeatureCollection? features, CancellationToken cancellationToken) =>
            Task.FromResult<ServiceAddress?>(id == "good" && _adapterId ? _serviceAddress : null);

        public Task<ServiceAddress?> FindObjectByIdAsync(string id, IFeatureCollection? features, CancellationToken cancellationToken) =>
            Task.FromResult<ServiceAddress?>(id == "good" && !_adapterId ? _serviceAddress : null);

        Task<LocatorRegistryProxy?> ILocatorProxy.GetRegistryAsync(IFeatureCollection? features, CancellationToken cancellationToken) =>
            throw new NotImplementedException();
    }

    private sealed class NotFoundLocatorProxy : ILocatorProxy
    {
        public Task<ServiceAddress?> FindAdapterByIdAsync(string id, IFeatureCollection? features, CancellationToken cancellationToken) =>
            throw new AdapterNotFoundException();

        public Task<ServiceAddress?> FindObjectByIdAsync(string id, IFeatureCollection? features, CancellationToken cancellationToken) =>
            throw new ObjectNotFoundException();

        public Task<LocatorRegistryProxy?> GetRegistryAsync(IFeatureCollection? features, CancellationToken cancellationToken) =>
            throw new NotImplementedException();
    }
}
