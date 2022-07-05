// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace IceRpc.Locator.Internal;

/// <summary>An endpoint cache maintains a dictionary of location to endpoint(s), where the endpoints are held by a
/// dummy service address. It also keeps track of the insertion time of each entry. It's consumed by
/// <see cref="LocationResolver"/>.</summary>
internal interface IEndpointCache
{
    void Remove(Location location);

    void Set(Location location, ServiceAddress serviceAddress);

    bool TryGetValue(Location location, out (TimeSpan InsertionTime, ServiceAddress ServiceAddress) value);
}

/// <summary>The main implementation for IEndpointCache.</summary>
internal sealed class EndpointCache : IEndpointCache
{
    private readonly ConcurrentDictionary<Location, (TimeSpan InsertionTime, ServiceAddress ServiceAddress, LinkedListNode<Location> Node)> _cache;

    // The keys in _cache. The first entries correspond to the most recently added cache entries.
    private readonly LinkedList<Location> _cacheKeys = new();

    private readonly int _maxCacheSize;

    // _mutex protects _cacheKeys and updates to _cache
    private readonly object _mutex = new();

    internal EndpointCache(int maxCacheSize)
    {
        Debug.Assert(maxCacheSize > 0);
        _maxCacheSize = maxCacheSize;
        _cache = new(concurrencyLevel: 1, capacity: _maxCacheSize + 1);
    }

    void IEndpointCache.Remove(Location location) => Remove(location);

    void IEndpointCache.Set(Location location, ServiceAddress serviceAddress)
    {
        lock (_mutex)
        {
            Remove(location); // remove existing cache entry if present

            _cache[location] =
                (TimeSpan.FromMilliseconds(Environment.TickCount64), serviceAddress, _cacheKeys.AddFirst(location));

            if (_cacheKeys.Count == _maxCacheSize + 1)
            {
                // drop last (oldest) entry
                Remove(_cacheKeys.Last!.Value);

                Debug.Assert(_cacheKeys.Count == _maxCacheSize); // removed the last entry
            }
        }
    }

    bool IEndpointCache.TryGetValue(
        Location location,
        out (TimeSpan InsertionTime, ServiceAddress ServiceAddress) value)
    {
        // no mutex lock: _cache is a concurrent dictionary and it's ok if it's updated while we read it

        if (_cache.TryGetValue(
            location,
            out (TimeSpan InsertionTime, ServiceAddress ServiceAddress, LinkedListNode<Location> Node) entry))
        {
            value.InsertionTime = entry.InsertionTime;
            value.ServiceAddress = entry.ServiceAddress;
            return true;
        }
        else
        {
            value = default;
            return false;
        }
    }

    private void Remove(Location location)
    {
        lock (_mutex)
        {
            if (_cache.TryRemove(
                location,
                out (TimeSpan InsertionTime, ServiceAddress ServiceAddress, LinkedListNode<Location> Node) entry))
            {
                _cacheKeys.Remove(entry.Node);
            }
        }
    }
}

/// <summary>A decorator that adds logging to an endpoint cache.</summary>
internal class LogEndpointCacheDecorator : IEndpointCache
{
    private readonly IEndpointCache _decoratee;
    private readonly ILogger _logger;

    internal LogEndpointCacheDecorator(IEndpointCache decoratee, ILogger logger)
    {
        _decoratee = decoratee;
        _logger = logger;
    }

    void IEndpointCache.Remove(Location location)
    {
        _decoratee.Remove(location);
        _logger.LogRemovedEntry(location.Kind, location);
    }

    void IEndpointCache.Set(Location location, ServiceAddress serviceAddress)
    {
        _decoratee.Set(location, serviceAddress);
        _logger.LogSetEntry(location.Kind, location, serviceAddress);
    }

    bool IEndpointCache.TryGetValue(
        Location location,
        out (TimeSpan InsertionTime, ServiceAddress ServiceAddress) value)
    {
        if (_decoratee.TryGetValue(location, out value))
        {
            _logger.LogFoundEntry(location.Kind, location, value.ServiceAddress);
            return true;
        }
        else
        {
            return false;
        }
    }
}
