// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace IceRpc.Internal
{
    /// <summary>An endpoint cache maintains a dictionary of location to endpoint(s), where the endpoints are held by a
    /// dummy proxy. It also keeps track of the insertion time of each entry. It's consumed by
    /// <see cref="LocationResolver"/>.</summary>
    internal interface IEndpointCache
    {
        void Remove(Location location);
        void Set(Location location, Proxy proxy);
        bool TryGetValue(Location location, out (TimeSpan InsertionTime, Proxy Proxy) value);
    }

    /// <summary>The main implementation for IEndpointCache.</summary>
    internal sealed class EndpointCache : IEndpointCache
    {
        // Used for testing
        internal int Count
        {
            get
            {
                lock (_mutex)
                {
                    Debug.Assert(_cache.Count == _cacheKeys.Count);
                    return _cache.Count;
                }
            }
        }

        private readonly ConcurrentDictionary<Location, (TimeSpan InsertionTime, Proxy Proxy, LinkedListNode<Location> Node)> _cache;

        // The keys in _cache. The first entries correspond to the most recently added cache entries.
        private readonly LinkedList<Location> _cacheKeys = new();

        private readonly int _cacheMaxSize;

        // _mutex protects _cacheKeys and updates to _cache
        private readonly object _mutex = new();

        internal EndpointCache(int cacheMaxSize)
        {
            Debug.Assert(cacheMaxSize > 0);
            _cacheMaxSize = cacheMaxSize;
            _cache = new(concurrencyLevel: 1, capacity: _cacheMaxSize + 1);
        }

        void IEndpointCache.Remove(Location location) => Remove(location);

        void IEndpointCache.Set(Location location, Proxy proxy)
        {
            lock (_mutex)
            {
                Remove(location); // remove existing cache entry if present

                _cache[location] = (Time.Elapsed, proxy, _cacheKeys.AddFirst(location));

                if (_cacheKeys.Count == _cacheMaxSize + 1)
                {
                    // drop last (oldest) entry
                    Remove(_cacheKeys.Last!.Value);

                    Debug.Assert(_cacheKeys.Count == _cacheMaxSize); // removed the last entry
                }
            }
        }

        bool IEndpointCache.TryGetValue(Location location, out (TimeSpan InsertionTime, Proxy Proxy) value)
        {
            // no mutex lock: _cache is a concurrent dictionary and it's ok if it's updated while we read it

            if (_cache.TryGetValue(location,
                                   out (TimeSpan InsertionTime, Proxy Proxy, LinkedListNode<Location> Node) entry))
            {
                value.InsertionTime = entry.InsertionTime;
                value.Proxy = entry.Proxy;
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
                if (_cache.TryRemove(location,
                                     out (TimeSpan InsertionTime, Proxy Proxy, LinkedListNode<Location> Node) entry))
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

        void IEndpointCache.Set(Location location, Proxy proxy)
        {
            _decoratee.Set(location, proxy);
            _logger.LogSetEntry(location.Kind, location, proxy);
        }

        bool IEndpointCache.TryGetValue(Location location, out (TimeSpan InsertionTime, Proxy Proxy) value)
        {
            if (_decoratee.TryGetValue(location, out value))
            {
                _logger.LogFoundEntry(location.Kind, location, value.Proxy);
                return true;
            }
            else
            {
                return false;
            }
        }
    }

    /// <summary>This class contains ILogger extension methods used by LogEndpointCacheDecorator.</summary>
    internal static partial class EndpointCacheLoggerExtensions
    {
        [LoggerMessage(
            EventId = (int)LocationEventIds.FoundEntry,
            EventName = nameof(LocationEventIds.FoundEntry),
            Level = LogLevel.Trace,
            Message = "found {LocationKind} '{Location}' = '{Proxy}' in cache")]
        internal static partial void LogFoundEntry(
            this ILogger logger,
            string locationKind,
            Location location,
            Proxy proxy);

        [LoggerMessage(
            EventId = (int)LocationEventIds.SetEntry,
            EventName = nameof(LocationEventIds.SetEntry),
            Level = LogLevel.Trace,
            Message = "set {LocationKind} '{Location}' = '{Proxy}' in cache")]
        internal static partial void LogSetEntry(
            this ILogger logger,
            string locationKind,
            Location location,
            Proxy proxy);

        [LoggerMessage(
            EventId = (int)LocationEventIds.RemovedEntry,
            EventName = nameof(LocationEventIds.RemovedEntry),
            Level = LogLevel.Trace,
            Message = "removed {LocationKind} '{Location}' from cache")]
        internal static partial void LogRemovedEntry(
            this ILogger logger,
            string locationKind,
            Location location);
    }
}
