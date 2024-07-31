// Copyright (c) ZeroC, Inc.

using IceRpc.Locator.Internal;
using NUnit.Framework;

namespace IceRpc.Locator.Tests;

public class LocationResolverTests
{
    [TestCase(120, 100)]
    [TestCase(120, 60)]
    [NonParallelizable]
    public async Task ServerAddress_finder_not_called_when_cache_entry_age_is_less_or_equal_than_refresh_threshold(
        int refreshThreshold,
        int cacheEntryAge)
    {
        var cachedServiceAddress = new ServiceAddress(new Uri("ice://localhost/cached"));
        var serverAddressFinder = new MockServerAddressFinder();
        var resolver = new LocationResolver(
                serverAddressFinder,
                new MockServerAddressCache(cachedServiceAddress, insertionTime: TimeSpan.FromSeconds(cacheEntryAge)),
                background: false,
                TimeSpan.FromSeconds(refreshThreshold),
                ttl: Timeout.InfiniteTimeSpan);

        (ServiceAddress? resolved, bool _) = await resolver.ResolveAsync(default, refreshCache: true, default);

        Assert.That(serverAddressFinder.Calls, Is.EqualTo(0));
        Assert.That(resolved, Is.EqualTo(cachedServiceAddress));
    }

    [TestCase(100, 120)]
    [TestCase(60, 120)]
    [NonParallelizable]
    public async Task ServerAddress_finder_called_when_cache_entry_age_is_greater_than_refresh_threshold(
        int refreshThreshold,
        int cacheEntryAge)
    {
        var cachedServiceAddress = new ServiceAddress(new Uri("ice://localhost/cached"));
        var resolvedServiceAddress = new ServiceAddress(new Uri("ice://localhost/resolved"));
        var serverAddressFinder = new MockServerAddressFinder(resolvedServiceAddress);
        var resolver = new LocationResolver(
                serverAddressFinder,
                new MockServerAddressCache(cachedServiceAddress, insertionTime: TimeSpan.FromSeconds(cacheEntryAge)),
                background: false,
                TimeSpan.FromSeconds(refreshThreshold),
                ttl: Timeout.InfiniteTimeSpan);

        (ServiceAddress? resolved, bool _) = await resolver.ResolveAsync(default, refreshCache: true, default);

        Assert.That(serverAddressFinder.Calls, Is.EqualTo(1));
        Assert.That(resolved, Is.EqualTo(resolvedServiceAddress));
    }

    [Test]
    [NonParallelizable]
    public async Task ServerAddress_finder_called_on_background()
    {
        var cachedServiceAddress = new ServiceAddress(new Uri("ice://localhost/stale"));
        var resolvedServiceAddress = new ServiceAddress(new Uri("ice://localhost/resolved"));
        var serverAddressFinder = new MockServerAddressFinder(resolvedServiceAddress);
        var resolver = new LocationResolver(
                serverAddressFinder,
                new MockServerAddressCache(cachedServiceAddress, insertionTime: TimeSpan.FromSeconds(120)),
                background: true,
                TimeSpan.FromSeconds(1),
                ttl: TimeSpan.FromSeconds(30));

        (ServiceAddress? resolved, bool fromCache) = await resolver.ResolveAsync(default, refreshCache: false, default);

        Assert.That(fromCache, Is.True);
        Assert.That(resolved, Is.EqualTo(cachedServiceAddress));
        Assert.That(serverAddressFinder.Calls, Is.EqualTo(1));
    }

    [Test]
    public async Task Location_recursive_resolution()
    {
        var wellKnownServiceAddress = new ServiceAddress(new Uri("ice:/foo?adapter-id=bar"));
        var adapterIdServiceAddress = new ServiceAddress(new Uri("ice://localhost/resolved"));
        var serverAddressFinder = new MockServerAddressFinder(wellKnownServiceAddress, adapterIdServiceAddress);
        var resolver = new LocationResolver(
                serverAddressFinder,
                new MockServerAddressCache(),
                background: true,
                TimeSpan.FromSeconds(1),
                ttl: TimeSpan.FromSeconds(30));

        (ServiceAddress? resolved, bool fromCache) = await resolver.ResolveAsync(
            new Location
            {
                Value = "/hello",
                IsAdapterId = false,
            },
            refreshCache: false,
            default);

        Assert.That(fromCache, Is.False);
        Assert.That(resolved, Is.EqualTo(adapterIdServiceAddress));
        Assert.That(serverAddressFinder.Calls, Is.EqualTo(2));
    }

    [Test]
    public async Task Failure_to_recursively_resolve_adapter_id_removes_proxy_from_cache()
    {
        var wellKnownServiceAddress = new ServiceAddress(new Uri("ice:/foo?adapter-id=bar"));
        var serverAddressFinder = new MockServerAddressFinder(wellKnownServiceAddress);
        var serverAddressCache = new MockServerAddressCache(wellKnownServiceAddress);
        var resolver = new LocationResolver(
                serverAddressFinder,
                serverAddressCache,
                background: true,
                TimeSpan.FromSeconds(1),
                ttl: TimeSpan.FromSeconds(30));
        var location = new Location
        {
            Value = "/hello",
            IsAdapterId = false,
        };

        (ServiceAddress? resolved, bool fromCache) = await resolver.ResolveAsync(
            location,
            refreshCache: false,
            default);

        Assert.That(fromCache, Is.False);
        Assert.That(resolved, Is.Null);
        Assert.That(serverAddressCache.Removed, Does.Contain(location));
        Assert.That(serverAddressFinder.Calls, Is.EqualTo(1));
    }

    private sealed class MockServerAddressFinder : IServerAddressFinder
    {
        public int Calls { get; private set; }

        private readonly ServiceAddress? _adapterIdServiceAddress;
        private readonly ServiceAddress? _wellKnownServiceAddress;

        internal MockServerAddressFinder(
            ServiceAddress? wellKnownServiceAddress = null,
            ServiceAddress? adapterIdServiceAddress = null)
        {
            _wellKnownServiceAddress = wellKnownServiceAddress;
            _adapterIdServiceAddress = adapterIdServiceAddress;
        }

        public Task<ServiceAddress?> FindAsync(Location location, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(location.IsAdapterId ? _adapterIdServiceAddress : _wellKnownServiceAddress);
        }
    }

    private sealed class MockServerAddressCache : IServerAddressCache
    {
        public List<Location> Removed { get; } = new List<Location>();

        private readonly TimeSpan _insertionTime;
        private readonly ServiceAddress? _adapterIdServiceAddress;
        private readonly ServiceAddress? _wellKnownServiceAddress;

        internal MockServerAddressCache(
            ServiceAddress? wellKnownServiceAddress = null,
            ServiceAddress? adapterIdServiceAddress = null,
            TimeSpan? insertionTime = null)
        {
            _wellKnownServiceAddress = wellKnownServiceAddress;
            _adapterIdServiceAddress = adapterIdServiceAddress;
            _insertionTime = insertionTime ?? Timeout.InfiniteTimeSpan;
        }
        public void Remove(Location location) => Removed.Add(location);
        public void Set(Location location, ServiceAddress serviceAddress) => throw new NotImplementedException();
        public bool TryGetValue(Location location, out (TimeSpan InsertionTime, ServiceAddress ServiceAddress) value)
        {
            if ((location.IsAdapterId && _adapterIdServiceAddress is null) ||
                (!location.IsAdapterId && _wellKnownServiceAddress is null))
            {
                value = default;
                return false;
            }
            else
            {
                value = (TimeSpan.FromMilliseconds(Environment.TickCount64) - _insertionTime,
                         location.IsAdapterId ? _adapterIdServiceAddress! : _wellKnownServiceAddress!);
                return true;
            }
        }
    }
}
