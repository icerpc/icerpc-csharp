// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Interop;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Internal
{
    /// <summary>Provides the implementation of
    /// <see cref="Interceptors.Locator(ILocatorPrx, Interceptors.LocatorOptions)"/>.</summary>
    internal sealed class LocatorClient
    {
        private readonly bool _background;
        private readonly ICache? _cache;
        private readonly TimeSpan _justRefreshedAge;
        private readonly ILocationResolver _locationResolver;
        private readonly ILogger _logger;
        private readonly TimeSpan _ttl;

        /// <summary>Constructs a locator invoker.</summary>
        internal LocatorClient(ILocatorPrx locator, Interceptors.LocatorOptions options)
        {
            if (options.Ttl != Timeout.InfiniteTimeSpan && options.JustRefreshedAge >= options.Ttl)
            {
                throw new ArgumentException(
                    $"{nameof(options.JustRefreshedAge)} must be smaller than {nameof(options.Ttl)}", nameof(options));
            }

            _background = options.Background;
            _justRefreshedAge = options.JustRefreshedAge;
            _logger = options.LoggerFactory.CreateLogger("IceRpc");
            _ttl = options.Ttl;

            if (_ttl != TimeSpan.Zero && options.CacheMaxSize > 0)
            {
                _cache = new LogCacheDecorator(new Cache(options.CacheMaxSize), _logger);
            }

            ILocationResolver locationResolver = new LocationResolver(locator);

            // Install decorators
            locationResolver = new LogDecorator(locationResolver, _logger);
            if (_cache != null)
            {
                locationResolver = new CacheUpdateDecorator(locationResolver, _cache);
            }
            locationResolver = new CoalesceDecorator(locationResolver);

            _locationResolver = locationResolver;
        }

        /// <summary>Updates the endpoints of the request (as appropriate) then call InvokeAsync on next.</summary>
        /// <param name="request">The outgoing request.</param>
        /// <param name="next">The next invoker in the pipeline.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>The response.</returns>
        internal async Task<IncomingResponse> InvokeAsync(
            OutgoingRequest request,
            IInvoker next,
            CancellationToken cancel)
        {
            if (request.Connection == null)
            {
                Key key = default;
                bool refreshCache = false;

                if (request.Features.Get<CachedResolutionFeature>() is CachedResolutionFeature cachedResolution)
                {
                    // This is the second (or greater) attempt, and we provided a cached resolution with the first
                    // attempt and all subsequent attempts.

                    key = cachedResolution.Key;
                    refreshCache = true;
                }
                else if (request.Endpoint is Endpoint locEndpoint && locEndpoint.Transport == TransportNames.Loc)
                {
                    // Typically first attempt since a successful resolution replaces this loc endpoint.
                    key = new Key(locEndpoint.Host);
                }
                else if (request.Endpoint == null && request.Protocol == Protocol.Ice1)
                {
                    // Well-known proxy
                    key = new Key(request.Identity);
                }

                if (key != default)
                {
                    _logger.LogResolving(key.Kind, key);

                    try
                    {
                        (Proxy? proxy, bool fromCache) =
                            await ResolveAsync(key, refreshCache, cancel).ConfigureAwait(false);

                        if (refreshCache)
                        {
                            if (!fromCache && !request.Features.IsReadOnly)
                            {
                                // No need to resolve the loc endpoint / identity again since we are not returning a
                                // cached value.
                                request.Features.Set<CachedResolutionFeature>(null);
                            }
                        }
                        else if (fromCache)
                        {
                            // Make sure the next attempt re-resolves key and sets refreshCache to true.

                            if (request.Features.IsReadOnly)
                            {
                                request.Features = new FeatureCollection(request.Features);
                            }
                            request.Features.Set(new CachedResolutionFeature(key));
                        }

                        if (proxy?.Endpoint != null)
                        {
                            _logger.LogResolved(key.Kind, key, proxy);

                            request.Endpoint = proxy.Endpoint;
                            request.AltEndpoints = proxy.AltEndpoints;
                        }
                        else
                        {
                            _logger.LogFailedToResolve(key.Kind, key);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogFailedToResolve(key.Kind, key, ex);
                    }
                }
            }

            return await next.InvokeAsync(request, cancel).ConfigureAwait(false);
        }

        private async ValueTask<(Proxy? Proxy, bool FromCache)> ResolveAsync(
            Key key,
            bool refreshCache,
            CancellationToken cancel)
        {
            Proxy? proxy = null;
            bool expired = false;
            bool justRefreshed = false;
            bool resolved = false;

            if (_cache != null && _cache.TryGetValue(key, out (TimeSpan InsertionTime, Proxy Proxy) entry))
            {
                proxy = entry.Proxy;
                TimeSpan cacheEntryAge = Time.Elapsed - entry.InsertionTime;
                expired = _ttl != Timeout.InfiniteTimeSpan && cacheEntryAge > _ttl;
                justRefreshed = cacheEntryAge <= _justRefreshedAge;
            }

            if (proxy == null || (!_background && expired) || (refreshCache && !justRefreshed))
            {
                proxy = await _locationResolver.FindAsync(key, cancel).ConfigureAwait(false);
                resolved = true;
            }
            else if (_background && expired)
            {
                // We retrieved an expired proxy from the cache, so we launch a refresh in the background.
                _ = _locationResolver.FindAsync(key, cancel: default).ConfigureAwait(false);
            }

            // A well-known proxy resolution can return a loc endpoint, but not another well-known proxy loc
            // endpoint.
            if (proxy?.Endpoint?.Transport == TransportNames.Loc)
            {
                try
                {
                    // Resolves adapter ID recursively, by checking first the cache. If we resolved the well-known
                    // proxy, we request a cache refresh for the adapter.
                    (proxy, _) = await ResolveAsync(new Key(proxy!.Endpoint!.Host),
                                                    refreshCache || resolved,
                                                    cancel).ConfigureAwait(false);
                }
                finally
                {
                    // When the second resolution fails, we clear the cache entry for the initial successful
                    // resolution, since the overall resolution is a failure.
                    // proxy below can hold a loc endpoint only when an exception is thrown.
                    if (proxy == null || proxy?.Endpoint?.Transport == TransportNames.Loc)
                    {
                        _cache?.Remove(key);
                    }
                }
            }

            return (proxy, proxy != null && !resolved);
        }

        // The type used for all lookups.
        internal readonly struct Key : IEquatable<Key>
        {
            internal readonly string Location;
            internal readonly string? Category;

            internal string Kind => Category == null ? "adapter ID" : "identity";

            public static bool operator ==(Key lhs, Key rhs) => lhs.Equals(rhs);
            public static bool operator !=(Key lhs, Key rhs) => !lhs.Equals(rhs);

            public override bool Equals(object? obj) => obj is Key value && Equals(value);
            public bool Equals(Key other) => Location == other.Location && Category == other.Category;
            public override int GetHashCode() => HashCode.Combine(Location, Category);

            public override string ToString() =>
                Category == null ? Location : new Identity(Location, Category).ToString();

            internal Identity ToIdentity() =>
                Category is string category ? new Identity(Location, category) : throw new InvalidOperationException();

            internal Key(string location)
            {
                Location = location;
                Category = null;
            }

            internal Key(Identity identity)
            {
                Location = identity.Name;
                Category = identity.Category;
            }
        }

        internal interface ILocationResolver
        {
            Task<Proxy?> FindAsync(Key key, CancellationToken cancel);
        }

        internal interface ICache
        {
            void Remove(Key key);
            void Set(Key key, Proxy proxy);

            bool TryGetValue(Key key, out (TimeSpan InsertionTime, Proxy Proxy) value);
        }

        internal class LocationResolver : ILocationResolver
        {
            private readonly ILocatorPrx _locator;

            internal LocationResolver(ILocatorPrx locator) => _locator = locator;

            async Task<Proxy?> ILocationResolver.FindAsync(Key key, CancellationToken cancel)
            {
                if (key.Category == null)
                {
                    try
                    {
                        ServicePrx? prx =
                            await _locator.FindAdapterByIdAsync(key.Location, cancel: cancel).ConfigureAwait(false);

                        if (prx?.Proxy is Proxy proxy)
                        {
                            if (proxy.IsIndirect)
                            {
                                throw new InvalidDataException($"findAdapterById returned invalid proxy '{proxy}'");
                            }
                            return proxy;
                        }
                        else
                        {
                            return null;
                        }
                    }
                    catch (AdapterNotFoundException)
                    {
                        // We treat AdapterNotFoundException just like a null return value.
                        return null;
                    }
                }
                else
                {
                    try
                    {
                        ServicePrx? prx =
                            await _locator.FindObjectByIdAsync(key.ToIdentity(), cancel: cancel).ConfigureAwait(false);

                        if (prx?.Proxy is Proxy proxy)
                        {
                            if (proxy.IsWellKnown || proxy.Protocol != Protocol.Ice1)
                            {
                                throw new InvalidDataException($"findObjectById returned invalid proxy '{proxy}'");
                            }
                            return proxy;
                        }
                        else
                        {
                            return null;
                        }
                    }
                    catch (ObjectNotFoundException)
                    {
                        // We treat ObjectNotFoundException just like a null return value.
                        return null;
                    }
                }
            }
        }

        internal class LogDecorator : ILocationResolver
        {
            private readonly ILocationResolver _decoratee;
            private readonly ILogger _logger;

            internal LogDecorator(ILocationResolver decoratee, ILogger logger)
            {
                _decoratee = decoratee;
                _logger = logger;
            }

            async Task<Proxy?> ILocationResolver.FindAsync(Key key, CancellationToken cancel)
            {
                try
                {
                    Proxy? proxy = await _decoratee.FindAsync(key, cancel).ConfigureAwait(false);

                    if (proxy != null)
                    {
                        Debug.Assert(proxy.Endpoint != null);
                        _logger.LogFound(key.Kind, key, proxy);
                    }
                    else
                    {
                        _logger.LogFindFailed(key.Kind, key);
                    }
                    return proxy;
                }
                catch
                {
                    // We log the exception itself when we actually handle it.
                    _logger.LogFindFailed(key.Kind, key);
                    throw;
                }
            }
        }

        // This decorator updates the cache after a remote call to the locator. It needs to execute downstream from the
        /// Coalesce decorator.
        internal class CacheUpdateDecorator : ILocationResolver
        {
            private readonly ICache _cache;
            private readonly ILocationResolver _decoratee;

            internal CacheUpdateDecorator(ILocationResolver decoratee, ICache cache)
            {
                _cache = cache;
                _decoratee = decoratee;
            }

            async Task<Proxy?> ILocationResolver.FindAsync(Key key, CancellationToken cancel)
            {
                Proxy? proxy = await _decoratee.FindAsync(key, cancel).ConfigureAwait(false);

                if (proxy != null)
                {
                    _cache.Set(key, proxy);
                }
                else
                {
                    _cache.Remove(key);
                }
                return proxy;
            }
        }

        // Detects multiple concurrent identical requests and "coalesce" them to avoid overloading the locator.
        internal class CoalesceDecorator : ILocationResolver
        {
            private readonly ILocationResolver _decoratee;
            private readonly object _mutex = new();
            private readonly Dictionary<Key, Task<Proxy?>> _requests = new();

            internal CoalesceDecorator(ILocationResolver decoratee) =>
                _decoratee = decoratee;

            Task<Proxy?> ILocationResolver.FindAsync(Key key, CancellationToken cancel)
            {
                Task<Proxy?>? task;

                lock (_mutex)
                {
                    if (!_requests.TryGetValue(key, out task))
                    {
                        // If there is no request in progress, we invoke one and cache the request to prevent concurrent
                        // identical requests. It's removed once the response is received.
                        task = PerformFindAsync();

                        if (!task.IsCompleted)
                        {
                            // If PerformFindAsync completed, don't add the task (it would leak since PerformFindAsync
                            // is responsible for removing it).
                            // Since PerformFindAsync locks _mutex in its finally block, the only way it can
                            // be completed now is if completed synchronously.
                            _requests.Add(key, task);
                        }
                    }
                }

                return task.WaitAsync(cancel);

                async Task<Proxy?> PerformFindAsync()
                {
                    try
                    {
                        return await _decoratee.FindAsync(key, cancel).ConfigureAwait(false);
                    }
                    finally
                    {
                        lock (_mutex)
                        {
                            _requests.Remove(key);
                        }
                    }
                }
            }
        }

        internal class Cache : ICache
        {
            private readonly ConcurrentDictionary<Key, (TimeSpan InsertionTime, Proxy Proxy, LinkedListNode<Key> Node)> _cache;

            // The keys in _cache. The first entries correspond to the most recently added cache entries.
            private readonly LinkedList<Key> _cacheKeys = new();

            private readonly int _cacheMaxSize;

            // _mutex protects _cacheKeys and updates to _cache
            private readonly object _mutex = new();

            internal Cache(int cacheMaxSize)
            {
                Debug.Assert(cacheMaxSize > 0);
                _cacheMaxSize = cacheMaxSize;
                _cache = new(concurrencyLevel: 1, capacity: _cacheMaxSize + 1);
            }

            void ICache.Remove(Key key) => Remove(key);

            void ICache.Set(Key key, Proxy proxy)
            {
                lock (_mutex)
                {
                    Remove(key); // remove existing cache entry if present

                    _cache[key] = (Time.Elapsed, proxy, _cacheKeys.AddFirst(key));

                    if (_cacheKeys.Count == _cacheMaxSize + 1)
                    {
                        // drop last (oldest) entry
                        Remove(_cacheKeys.Last!.Value);

                        Debug.Assert(_cacheKeys.Count == _cacheMaxSize); // removed the last entry
                    }
                }
            }

            bool ICache.TryGetValue(Key key, out (TimeSpan InsertionTime, Proxy Proxy) value)
            {
                // no mutex lock: _cache is a concurrent dictionary and it's ok if it's updated while we read it

                if (_cache.TryGetValue(key, out (TimeSpan InsertionTime, Proxy Proxy, LinkedListNode<Key> Node) entry))
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

            private void Remove(Key key)
            {
                lock (_mutex)
                {
                    if (_cache.TryRemove(key,
                                         out (TimeSpan InsertionTime, Proxy Proxy, LinkedListNode<Key> Node) entry))
                    {
                        _cacheKeys.Remove(entry.Node);
                    }
                }
            }
        }

        internal class LogCacheDecorator : ICache
        {
            private readonly ICache _decoratee;
            private readonly ILogger _logger;

            internal LogCacheDecorator(ICache decoratee, ILogger logger)
            {
                _decoratee = decoratee;
                _logger = logger;
            }

            void ICache.Remove(Key key)
            {
                _decoratee.Remove(key);
                _logger.LogRemovedEntryFromCache(key.Kind, key);
            }

            void ICache.Set(Key key, Proxy proxy)
            {
                _decoratee.Set(key, proxy);
                _logger.LogSetEntryInCache(key.Kind, key, proxy);
            }

            bool ICache.TryGetValue(Key key, out (TimeSpan InsertionTime, Proxy Proxy) value)
            {
                if (_decoratee.TryGetValue(key, out value))
                {
                    _logger.LogFoundEntryInCache(key.Kind, key, value.Proxy);
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

        private sealed class CachedResolutionFeature
        {
            internal Key Key { get; }

            internal CachedResolutionFeature(Key key) => Key = key;
        }
    }
}
