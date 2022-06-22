// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Locator.Internal;
using NUnit.Framework;

namespace IceRpc.Locator.Tests;

public class LocationResolverTests
{
    [TestCase(120, 100)]
    [TestCase(120, 60)]
    [NonParallelizable]
    public async Task Endpoint_finder_not_called_when_cache_entry_age_is_less_or_equal_than_refresh_threshold(
        int refreshThreshold,
        int cacheEntryAge)
    {
        var cachedProxy = Proxy.Parse("ice://localhost/cached");
        var endpointFinder = new MockEndpointFinder();
        var resolver = new LocationResolver(
                endpointFinder,
                new MockEndpointCache(cachedProxy, insertionTime: TimeSpan.FromSeconds(cacheEntryAge)),
                background: false,
                TimeSpan.FromSeconds(refreshThreshold),
                ttl: Timeout.InfiniteTimeSpan);

        (Proxy? resolved, bool _) = await resolver.ResolveAsync(new Location(), refreshCache: true, default);

        Assert.That(endpointFinder.Calls, Is.EqualTo(0));
        Assert.That(resolved, Is.EqualTo(cachedProxy));
    }

    [TestCase(100, 120)]
    [TestCase(60, 120)]
    [NonParallelizable]
    public async Task Endpoint_finder_called_when_cache_entry_age_is_greater_than_refresh_threshold(
        int refreshThreshold,
        int cacheEntryAge)
    {
        var cachedProxy = Proxy.Parse("ice://localhost/cached");
        var resolvedProxy = Proxy.Parse("ice://localhost/resolved");
        var endpointFinder = new MockEndpointFinder(resolvedProxy);
        var resolver = new LocationResolver(
                endpointFinder,
                new MockEndpointCache(cachedProxy, insertionTime: TimeSpan.FromSeconds(cacheEntryAge)),
                background: false,
                TimeSpan.FromSeconds(refreshThreshold),
                ttl: Timeout.InfiniteTimeSpan);

        (Proxy? resolved, bool _) = await resolver.ResolveAsync(new Location(), refreshCache: true, default);

        Assert.That(endpointFinder.Calls, Is.EqualTo(1));
        Assert.That(resolved, Is.EqualTo(resolvedProxy));
    }

    [Test]
    [NonParallelizable]
    public async Task Endpoint_finder_called_on_background()
    {
        var cachedProxy = Proxy.Parse("ice://localhost/stale");
        var resolvedProxy = Proxy.Parse("ice://localhost/resolved");
        var endpointFinder = new MockEndpointFinder(resolvedProxy);
        var resolver = new LocationResolver(
                endpointFinder,
                new MockEndpointCache(cachedProxy, insertionTime: TimeSpan.FromSeconds(120)),
                background: true,
                TimeSpan.FromSeconds(1),
                ttl: TimeSpan.FromSeconds(30));

        (Proxy? resolved, bool fromCache) = await resolver.ResolveAsync(new Location(), refreshCache: false, default);

        Assert.That(fromCache, Is.True);
        Assert.That(resolved, Is.EqualTo(cachedProxy));
        Assert.That(endpointFinder.Calls, Is.EqualTo(1));
    }

    [Test]
    public async Task Location_recursive_resolution()
    {
        var wellKnownProxy = Proxy.Parse("ice:/foo?adapter-id=bar");
        var adapterIdProxy = Proxy.Parse("ice://localhost/resolved");
        var endpointFinder = new MockEndpointFinder(wellKnownProxy, adapterIdProxy);
        var resolver = new LocationResolver(
                endpointFinder,
                new MockEndpointCache(),
                background: true,
                TimeSpan.FromSeconds(1),
                ttl: TimeSpan.FromSeconds(30));

        (Proxy? resolved, bool fromCache) = await resolver.ResolveAsync(
            new Location
            {
                Value = "/hello",
                IsAdapterId = false,
            },
            refreshCache: false,
            default);

        Assert.That(fromCache, Is.False);
        Assert.That(resolved, Is.EqualTo(adapterIdProxy));
        Assert.That(endpointFinder.Calls, Is.EqualTo(2));
    }

    [Test]
    public async Task Failure_to_recursively_resolve_adapter_id_removes_proxy_from_cache()
    {
        var wellKnownProxy = Proxy.Parse("ice:/foo?adapter-id=bar");
        var endpointFinder = new MockEndpointFinder(wellKnownProxy);
        var endpointCache = new MockEndpointCache(wellKnownProxy);
        var resolver = new LocationResolver(
                endpointFinder,
                endpointCache,
                background: true,
                TimeSpan.FromSeconds(1),
                ttl: TimeSpan.FromSeconds(30));
        var location = new Location
        {
            Value = "/hello",
            IsAdapterId = false,
        };

        (Proxy? resolved, bool fromCache) = await resolver.ResolveAsync(
            location,
            refreshCache: false,
            default);

        Assert.That(fromCache, Is.False);
        Assert.That(resolved, Is.Null);
        Assert.That(endpointCache.Removed.Contains(location), Is.True);
        Assert.That(endpointFinder.Calls, Is.EqualTo(1));
    }

    private class MockEndpointFinder : IEndpointFinder
    {
        public int Calls { get; private set; }

        private readonly Proxy? _adapterIdProxy;
        private readonly Proxy? _wellKnownProxy;

        internal MockEndpointFinder(
            Proxy? wellKnownProxy = null,
            Proxy? adapterIdProxy = null)
        {
            _wellKnownProxy = wellKnownProxy;
            _adapterIdProxy = adapterIdProxy;
        }

        public Task<Proxy?> FindAsync(Location location, CancellationToken cancel)
        {
            Calls++;
            return Task.FromResult(location.IsAdapterId ? _adapterIdProxy : _wellKnownProxy);
        }
    }

    private class MockEndpointCache : IEndpointCache
    {
        public List<Location> Removed { get; } = new List<Location>();

        private readonly TimeSpan _insertionTime;
        private readonly Proxy? _adapterIdProxy;
        private readonly Proxy? _wellKnownProxy;

        internal MockEndpointCache(
            Proxy? wellKnownProxy = null,
            Proxy? adapterIdProxy = null,
            TimeSpan? insertionTime = null)
        {
            _wellKnownProxy = wellKnownProxy;
            _adapterIdProxy = adapterIdProxy;
            _insertionTime = insertionTime ?? Timeout.InfiniteTimeSpan;
        }
        public void Remove(Location location) => Removed.Add(location);
        public void Set(Location location, Proxy proxy) => throw new NotImplementedException();
        public bool TryGetValue(Location location, out (TimeSpan InsertionTime, Proxy Proxy) value)
        {
            if ((location.IsAdapterId && _adapterIdProxy is null) ||
                (!location.IsAdapterId && _wellKnownProxy is null))
            {
                value = default;
                return false;
            }
            else
            {
                value = (TimeSpan.FromMilliseconds(Environment.TickCount64) - _insertionTime,
                         location.IsAdapterId ? _adapterIdProxy! : _wellKnownProxy!);
                return true;
            }
        }
    }
}
