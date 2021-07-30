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
        internal interface ILocationResolver<T> where T : IEquatable<T>
        {
            Task<Proxy?> FindAsync(T location, CancellationToken cancel);
        }

        internal interface ILocationResolverWithCache<T> where T : IEquatable<T>
        {
            ValueTask<(Proxy? Proxy, bool FromCache)> FindAsync(
                T location,
                bool refreshCache,
                CancellationToken cancel);
        }

        internal class AdapterIdResolver : ILocationResolver<string>
        {
            private readonly string _adapterId;
            private readonly ILocatorPrx _locator;

            internal AdapterIdResolver(string adapterId, ILocatorPrx locator)
            {
                _adapterId = adapterId;
                _locator = locator;
            }

            async Task<Proxy?> ILocationResolver<string>.FindAsync(string location, CancellationToken cancel)
            {
                try
                {
                    ServicePrx? prx =
                        await _locator.FindAdapterByIdAsync(location, cancel: cancel).ConfigureAwait(false);

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
        }

        internal class IdentityResolver : ILocationResolver<Identity>
        {
            private readonly Identity _identity;
            private readonly ILocatorPrx _locator;

            internal IdentityResolver(Identity identity, ILocatorPrx locator)
            {
                _identity = identity;
                _locator = locator;
            }

            async Task<Proxy?> ILocationResolver<Identity>.FindAsync(Identity location, CancellationToken cancel)
            {
                try
                {
                    ServicePrx? prx =
                        await _locator.FindObjectByIdAsync(location, cancel: cancel).ConfigureAwait(false);

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

        internal class LogDecorator<T> : ILocationResolver<T> where T : IEquatable<T>
        {
            private readonly ILocationResolver<T> _decoratee;
            private readonly ILogger _logger;

            internal LogDecorator(ILocationResolver<T> decoratee, ILogger logger)
            {
                _decoratee = decoratee;
                _logger = logger;
            }

            async Task<Proxy?> ILocationResolver<T>.FindAsync(T location, CancellationToken cancel)
            {
                try
                {
                    Proxy? proxy = await _decoratee.FindAsync(location, cancel).ConfigureAwait(false);

                    if (proxy != null)
                    {
                        Debug.Assert(proxy.Endpoint != null);

                        _logger.LogFound(location.ToString()!, proxy.Endpoint, proxy.AltEndpoints);
                    }
                    else
                    {
                        _logger.LogFindFailed(location.ToString()!);
                    }

                    return proxy;
                }
                catch (Exception ex)
                {
                    _logger.LogFindFailedWithException(location.ToString()!, ex);
                    throw;
                }
            }
        }

        internal class CoalesceDecorator<T> : ILocationResolver<T> where T : IEquatable<T>
        {
            private readonly ILocationResolver<T> _decoratee;
            private readonly object _mutex = new();
            private readonly Dictionary<T, Task<Proxy?>> _requests = new();

            internal CoalesceDecorator(ILocationResolver<T> decoratee) =>
                _decoratee = decoratee;

            Task<Proxy?> ILocationResolver<T>.FindAsync(T location, CancellationToken cancel)
            {
                Task<Proxy?>? task;

                lock (_mutex)
                {
                    if (!_requests.TryGetValue(location, out task))
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
                            _requests.Add(location, task);
                        }
                    }
                }

                return task.WaitAsync(cancel);

                async Task<Proxy?> PerformFindAsync()
                {
                    try
                    {
                        return await _decoratee.FindAsync(location, cancel).ConfigureAwait(false);
                    }
                    finally
                    {
                        lock (_mutex)
                        {
                            _requests.Remove(location);
                        }
                    }
                }
            }
        }

        internal class CacheDecorator<T> : ILocationResolverWithCache<T> where T : IEquatable<T>
        {
            private readonly ILocationResolverWithCache<string>? _adapterIdResolver;
            private readonly bool _background;
            private readonly ConcurrentDictionary<T, (TimeSpan InsertionTime, Proxy Proxy, LinkedListNode<T> Node)> _cache;

            // The keys in _cache. The first entries correspond to the most recently added cache entries.
            private readonly LinkedList<T> _cacheKeys = new();
            private readonly int _cacheMaxSize;
            private readonly ILocationResolver<T> _decoratee;
            private readonly TimeSpan _justRefreshedAge;
            private readonly object _mutex = new();
            private readonly TimeSpan _ttl;

            internal CacheDecorator(
                ILocationResolver<T> decoratee,
                ILocationResolverWithCache<string>? adapterIdResolver,
                Interceptors.LocatorOptions options)
            {
                if (options.Ttl != Timeout.InfiniteTimeSpan && options.JustRefreshedAge >= options.Ttl)
                {
                    throw new ArgumentException(
                        $"{nameof(options.JustRefreshedAge)} must be smaller than {nameof(options.Ttl)}",
                        nameof(options));
                }

                _adapterIdResolver = adapterIdResolver;
                _background = options.Background;
                _cacheMaxSize = options.CacheMaxSize;
                _cache = new(concurrencyLevel: 1, capacity: _cacheMaxSize + 1);
                _decoratee = decoratee;
                _justRefreshedAge = options.JustRefreshedAge;
                _ttl = options.Ttl;
            }

            async ValueTask<(Proxy? Proxy, bool FromCache)> ILocationResolverWithCache<T>.FindAsync(
                T location,
                bool refreshCache,
                CancellationToken cancel)
            {
                // We check the cache even when refreshCache is true!
                bool expired = false;
                bool justRefreshed = false;
                Proxy? proxy = null;
                bool resolved = false;

                if (_cache.TryGetValue(location, out var entry))
                {
                    TimeSpan cacheEntryAge = Time.Elapsed - entry.InsertionTime;
                    expired = _ttl != Timeout.InfiniteTimeSpan && cacheEntryAge > _ttl;
                    justRefreshed = cacheEntryAge <= _justRefreshedAge;
                    proxy = entry.Proxy;
                }

                if (proxy == null || (!_background && expired) || (refreshCache && !justRefreshed))
                {
                    proxy = await PerformFindAsync(cancel).ConfigureAwait(false);
                    resolved = true;
                }
                else if (_background && expired)
                {
                    // We retrieved expired endpoints from the cache, so we launch a refresh in the background.
                    _ =  PerformFindAsync(cancel: default);
                }

                // A well-known proxy resolution can return a loc endpoint
                if (proxy?.Endpoint is Endpoint endpoint && endpoint.Transport == TransportNames.Loc)
                {
                    try
                    {
                        if (_adapterIdResolver != null)
                        {
                            // Resolves adapter ID recursively, by checking first the cache. If we resolved the
                            // identity, we request a cache refresh for the adapter.
                            (proxy, _) = await _adapterIdResolver.FindAsync(endpoint.Host,
                                                                            refreshCache || resolved,
                                                                            cancel).ConfigureAwait(false);

                        }
                    }
                    finally
                    {
                        // When the second resolution fails, we clear the cache entry for the initial successful
                        // resolution, since the overall resolution is a failure.
                        // proxy below can hold a loc endpoint only when an exception is thrown.
                        if (proxy == null || proxy?.Endpoint?.Transport == TransportNames.Loc)
                        {
                            ClearCache(location);
                        }
                    }
                }

                return (proxy, proxy != null && !resolved);

                async Task<Proxy?> PerformFindAsync(CancellationToken cancel)
                {
                    Proxy? proxy = await _decoratee.FindAsync(location, cancel: default);

                    if (proxy == null)
                    {
                        ClearCache(location);
                    }
                    else
                    {
                        lock (_mutex)
                        {
                            ClearCache(location); // remove existing cache entry if present

                            _cache[location] = (Time.Elapsed, proxy, _cacheKeys.AddFirst(location));

                            if (_cacheKeys.Count == _cacheMaxSize + 1)
                            {
                                // drop last (oldest) entry
                                T lastLocation = _cacheKeys.Last!.Value;
                                ClearCache(lastLocation);

                                Debug.Assert(_cacheKeys.Count == _cacheMaxSize); // removed the last entry
                            }
                        }
                    }
                    return proxy;
                }
            }

            private void ClearCache(T location)
            {
                lock (_mutex)
                {
                    if (_cache.TryRemove(location, out var entry))
                    {
                        _cacheKeys.Remove(entry.Node);
                    }
                }
            }
        }

        private bool HasCache => _ttl != TimeSpan.Zero && _cacheMaxSize > 0;
        private readonly bool _background;
        private readonly ConcurrentDictionary<(string Location, string? Category), (TimeSpan InsertionTime, Endpoint Endpoint, ImmutableList<Endpoint> AltEndpoints, LinkedListNode<(string Location, string? Category)> Node)> _cache;

        // The keys in _cache. The first entries correspond to the most recently added cache entries.
        private readonly LinkedList<(string Location, string? Category)> _cacheKeys = new();

        private readonly int _cacheMaxSize;

        private readonly TimeSpan _justRefreshedAge;

        private readonly ILocatorPrx _locator;

        private readonly ILogger _logger;

        // _mutex protects _cacheKeys, _requests and updates to _cache
        private readonly object _mutex = new();

        private readonly Dictionary<(string Location, string? Category), Task<(Endpoint?, ImmutableList<Endpoint>)>> _requests =
            new();

        private readonly TimeSpan _ttl;

        /// <summary>Constructs a locator invoker.</summary>
        internal LocatorClient(ILocatorPrx locator, Interceptors.LocatorOptions options)
        {
            if (options.Ttl != Timeout.InfiniteTimeSpan && options.JustRefreshedAge >= options.Ttl)
            {
                throw new ArgumentException(
                    $"{nameof(options.JustRefreshedAge)} must be smaller than {nameof(options.Ttl)}", nameof(options));
            }

            _locator = locator;
            _background = options.Background;
            _cacheMaxSize = options.CacheMaxSize;
            _cache = new(concurrencyLevel: 1, capacity: _cacheMaxSize + 1);
            _justRefreshedAge = options.JustRefreshedAge;
            _logger = options.LoggerFactory.CreateLogger("IceRpc");
            _ttl = options.Ttl;
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
                string? location = null;
                string? category = null;
                bool refreshCache = false;

                if (request.Features.Get<CachedResolutionFeature>() is CachedResolutionFeature cachedResolution)
                {
                    // This is the second (or greater) attempt, and we provided a cached resolution with the first
                    // attempt and all subsequent attempts.

                    location = cachedResolution.Location;
                    category = cachedResolution.Category;
                    refreshCache = true;
                }
                else if (request.Endpoint is Endpoint locEndpoint && locEndpoint.Transport == TransportNames.Loc)
                {
                    // Typically first attempt since a successful resolution replaces this loc endpoint.
                    location = locEndpoint.Host;
                }
                else if (request.Endpoint == null && request.Protocol == Protocol.Ice1)
                {
                    // Well-known proxy
                    location = request.Identity.Name;
                    category = request.Identity.Category;
                }

                if (location != null)
                {
                    (Endpoint? endpoint, IEnumerable<Endpoint> altEndpoints, bool fromCache) =
                        await ResolveAsync(location, category, refreshCache, cancel).ConfigureAwait(false);

                    if (refreshCache)
                    {
                        if (!fromCache && !request.Features.IsReadOnly)
                        {
                            // No need to resolve the loc endpoint / identity again since we didn't returned a cached
                            // value.
                            request.Features.Set<CachedResolutionFeature>(null);
                        }
                    }
                    else if (fromCache)
                    {
                        // Make sure the next attempt re-resolves location+category and sets refreshCache to true.

                        if (request.Features.IsReadOnly)
                        {
                            request.Features = new FeatureCollection(request.Features);
                        }
                        request.Features.Set(new CachedResolutionFeature(location, category));
                    }

                    if (endpoint != null)
                    {
                        request.Endpoint = endpoint;
                        request.AltEndpoints = altEndpoints;
                    }
                    // else the resolution failed and we don't change the endpoints of the request
                }
            }

            return await next.InvokeAsync(request, cancel).ConfigureAwait(false);
        }

        private void ClearCache(string location, string? category)
        {
            if (HasCache)
            {
                lock (_mutex)
                {
                    if (_cache.TryRemove(
                        (location, category),
                        out (TimeSpan _, Endpoint Endpoint, ImmutableList<Endpoint> AltEndpoints, LinkedListNode<(string, string?)> Node) entry))
                    {
                        _cacheKeys.Remove(entry.Node);
                        if (category == null)
                        {
                            _logger.LogClearAdapterIdCacheEntry(location, entry.Endpoint, entry.AltEndpoints);
                        }
                        else
                        {
                            _logger.LogClearWellKnownCacheEntry(new Identity(location, category),
                                                                             entry.Endpoint,
                                                                             entry.AltEndpoints);
                        }
                    }
                }
            }
        }

        private async ValueTask<(Endpoint?, ImmutableList<Endpoint>, bool FromCache)> ResolveAsync(
            string location,
            string? category,
            bool refreshCache,
            CancellationToken cancel)
        {
            Endpoint? endpoint = null;
            ImmutableList<Endpoint> altEndpoints = ImmutableList<Endpoint>.Empty;
            bool expired = false;
            bool justRefreshed = false;
            bool resolved = false;

            if (HasCache && _cache.TryGetValue(
                (location, category),
                out (TimeSpan InsertionTime, Endpoint Endpoint, ImmutableList<Endpoint> AltEndpoints, LinkedListNode<(string, string?)> _) entry))
            {
                endpoint = entry.Endpoint;
                altEndpoints = entry.AltEndpoints;
                TimeSpan cacheEntryAge = Time.Elapsed - entry.InsertionTime;
                expired = _ttl != Timeout.InfiniteTimeSpan && cacheEntryAge > _ttl;
                justRefreshed = cacheEntryAge <= _justRefreshedAge;
            }

            if (endpoint == null || (!_background && expired) || (refreshCache && !justRefreshed))
            {
                (endpoint, altEndpoints) =
                    await ResolveWithLocatorAsync(location, category, cancel).ConfigureAwait(false);
                resolved = true;
            }
            else if (_background && expired)
            {
                // We retrieved expired endpoints from the cache, so we launch a refresh in the background.
                _ = ResolveWithLocatorAsync(location, category, cancel: default);
            }

            // A well-known proxy resolution can return a loc endpoint, but not another well-known proxy loc
            // endpoint.
            if (endpoint?.Transport == TransportNames.Loc)
            {
                try
                {
                    // Resolves adapter ID recursively, by checking first the cache. If we resolved the endpoint, we
                    // request a cache refresh for the adapter.
                    (endpoint, altEndpoints, _) = await ResolveAsync(endpoint!.Host,
                                                                     category: null,
                                                                     refreshCache || resolved,
                                                                     cancel).ConfigureAwait(false);
                }
                finally
                {
                    // When the second resolution fails, we clear the cache entry for the initial successful
                    // resolution, since the overall resolution is a failure.
                    // endpoint below can hold a loc endpoint only when an exception is thrown.
                    if (endpoint == null || endpoint.Transport == TransportNames.Loc)
                    {
                        ClearCache(location, category);
                    }
                }
            }

            if (endpoint != null)
            {
                if (resolved)
                {
                    if (category == null)
                    {
                        _logger.LogResolvedAdapterId(location, endpoint, altEndpoints);
                    }
                    else
                    {
                        _logger.LogResolvedWellKnown(new Identity(location, category), endpoint, altEndpoints);
                    }
                }
                else
                {
                    if (category == null)
                    {
                        _logger.LogFoundAdapterIdEntryInCache(location, endpoint, altEndpoints);
                    }
                    else
                    {
                        _logger.LogFoundWellKnownEntryInCache(new Identity(location, category), endpoint, altEndpoints);
                    }
                }
            }
            else
            {
                if (category == null)
                {
                    _logger.LogCouldNotResolveAdapterId(location);
                }
                else
                {
                    _logger.LogCouldNotResolveWellKnown(new Identity(location, category));
                }
            }

            return (endpoint, altEndpoints, endpoint != null && !resolved);
        }

        private async Task<(Endpoint?, ImmutableList<Endpoint>)> ResolveWithLocatorAsync(
            string location,
            string? category,
            CancellationToken cancel)
        {
            if (category == null)
            {
                _logger.LogResolvingAdapterId(location);
            }
            else
            {
                _logger.LogResolvingWellKnown(new Identity(location, category));
            }

            Task<(Endpoint?, ImmutableList<Endpoint>)>? task;
            lock (_mutex)
            {
                if (!_requests.TryGetValue((location, category), out task))
                {
                    // If there is no request in progress, we invoke one and cache the request to prevent concurrent
                    // identical requests. It's removed once the response is received.
                    task = PerformResolveWithLocatorAsync();
                    if (!task.IsCompleted)
                    {
                        // If PerformResolveWithLocatorAsync completed, don't add the task (it would leak since
                        // PerformResolveWithLocatorAsync is responsible for removing it).
                        // Since PerformResolveWithLocatorAsync locks _mutex in its finally block, the only way it can
                        // be completed now is if completed synchronously.
                        _requests.Add((location, category), task);
                    }
                }
            }

            return await task.WaitAsync(cancel).ConfigureAwait(false);

            async Task<(Endpoint?, ImmutableList<Endpoint>)> PerformResolveWithLocatorAsync()
            {
                try
                {
                    ServicePrx? servicePrx = null;

                    if (category == null)
                    {
                        try
                        {
                            servicePrx = await _locator.FindAdapterByIdAsync(
                                location,
                                cancel: CancellationToken.None).ConfigureAwait(false);
                        }
                        catch (AdapterNotFoundException)
                        {
                            // We treat AdapterNotFoundException just like a null return value.
                            servicePrx = null;
                        }
                    }
                    else
                    {
                        try
                        {
                            servicePrx = await _locator.FindObjectByIdAsync(
                                new Identity(location, category),
                                cancel: CancellationToken.None).ConfigureAwait(false);
                        }
                        catch (ObjectNotFoundException)
                        {
                            // We treat ObjectNotFoundException just like a null return value.
                            servicePrx = null;
                        }
                    }

                    Proxy? resolved = servicePrx?.Proxy;

                    if (resolved != null &&
                        ((category == null && resolved.IsIndirect) ||
                          resolved.IsWellKnown ||
                          resolved.Protocol != Protocol.Ice1))
                    {
                        if (category == null)
                        {
                            _logger.LogReceivedInvalidProxyForAdapterId(location, resolved);
                        }
                        else
                        {
                            _logger.LogReceivedInvalidProxyForWellKnown(new Identity(location, category), resolved);
                        }
                        resolved = null;
                    }

                    if (resolved?.Endpoint == null)
                    {
                        ClearCache(location, category);
                    }
                    else if (HasCache)
                    {
                        // We need to insert the cache entry before removing the request from _requests (see finally
                        // below) to avoid a race condition.
                        lock (_mutex)
                        {
                            ClearCache(location, category); // remove existing cache entry if present

                            _cache[(location, category)] = (Time.Elapsed,
                                                            resolved.Endpoint,
                                                            resolved.AltEndpoints,
                                                            _cacheKeys.AddFirst((location, category)));

                            if (_cacheKeys.Count == _cacheMaxSize + 1)
                            {
                                // drop last (oldest) entry
                                (string lastLocation, string? lastCategory) = _cacheKeys.Last!.Value;
                                ClearCache(lastLocation, lastCategory);

                                Debug.Assert(_cacheKeys.Count == _cacheMaxSize); // removed the last entry
                            }
                        }
                    }
                    return (resolved?.Endpoint, resolved?.AltEndpoints ?? ImmutableList<Endpoint>.Empty);
                }
                catch (Exception exception)
                {
                    if (category == null)
                    {
                        _logger.LogResolveAdapterIdFailure(location, exception);
                    }
                    else
                    {
                        _logger.LogResolveWellKnownFailure(new Identity(location, category), exception);
                    }
                    throw;
                }
                finally
                {
                    lock (_mutex)
                    {
                        _requests.Remove((location, category));
                    }
                }
            }
        }

        private sealed class CachedResolutionFeature
        {
            internal string Location { get; }
            internal string? Category { get; }

            internal CachedResolutionFeature(string location, string? category)
            {
                Location = location;
                Category = category;
            }
        }
    }
}
