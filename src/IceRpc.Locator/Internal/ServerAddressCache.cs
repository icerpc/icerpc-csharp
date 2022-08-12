// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics;

namespace IceRpc.Locator.Internal;

/// <summary>A server address cache maintains a dictionary of location to server address(es), where the server
/// addresses are held by a dummy service address. It also keeps track of the insertion time of each entry. It's
/// consumed by <see cref="LocationResolver"/>.</summary>
internal interface IServerAddressCache
{
    void Remove(Location location);

    void Set(Location location, ServiceAddress serviceAddress);

    bool TryGetValue(Location location, out (TimeSpan InsertionTime, ServiceAddress ServiceAddress) value);
}

/// <summary>The main implementation for <see cref="IServerAddressCache"/>.</summary>
internal sealed class ServerAddressCache : IServerAddressCache
{
    private readonly ConcurrentDictionary<Location, (TimeSpan InsertionTime, ServiceAddress ServiceAddress, LinkedListNode<Location> Node)> _cache;

    // The keys in _cache. The first entries correspond to the most recently added cache entries.
    private readonly LinkedList<Location> _cacheKeys = new();

    private readonly int _maxCacheSize;

    // _mutex protects _cacheKeys and updates to _cache
    private readonly object _mutex = new();

    public void Remove(Location location)
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

    public void Set(Location location, ServiceAddress serviceAddress)
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

    public bool TryGetValue(Location location, out (TimeSpan InsertionTime, ServiceAddress ServiceAddress) value)
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

    internal ServerAddressCache(int maxCacheSize)
    {
        Debug.Assert(maxCacheSize > 0);
        _maxCacheSize = maxCacheSize;
        _cache = new(concurrencyLevel: 1, capacity: _maxCacheSize + 1);
    }
}

/// <summary>A decorator that adds event source logging to a server address cache.</summary>
internal class LogServerAddressCacheDecorator : IServerAddressCache
{
    private readonly IServerAddressCache _decoratee;

    public void Remove(Location location)
    {
        _decoratee.Remove(location);
        LocatorEventSource.Log.RemoveCacheEntry(location);
    }

    public void Set(Location location, ServiceAddress serviceAddress)
    {
        _decoratee.Set(location, serviceAddress);
        LocatorEventSource.Log.SetCacheEntry(location, serviceAddress);
    }

    public bool TryGetValue(
        Location location,
        out (TimeSpan InsertionTime, ServiceAddress ServiceAddress) value)
    {
        if (_decoratee.TryGetValue(location, out value))
        {
            LocatorEventSource.Log.FindCacheEntry(location, value.ServiceAddress);
            return true;
        }
        else
        {
            LocatorEventSource.Log.FindCacheEntry(location, null);
            return false;
        }
    }

    internal LogServerAddressCacheDecorator(IServerAddressCache decoratee) => _decoratee = decoratee;
}
