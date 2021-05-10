// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Interop;
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
    /// <summary>The implementation of <see cref="Interceptors.Locator(ILocatorPrx, Interceptors.LocatorOptions)"/>.
    /// </summary>
    internal sealed class LocatorInvoker : IInvoker
    {
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

        private readonly IInvoker _next;

        private readonly Dictionary<(string Location, string? Category), Task<(Endpoint?, ImmutableList<Endpoint>)>> _requests =
            new();

        private readonly TimeSpan _ttl;

        public async Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel)
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
                else if (request.Endpoint is Endpoint locEndpoint && locEndpoint.Transport == Transport.Loc)
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
                        if (!fromCache)
                        {
                            // No need to resolve the loc endpoint / identity again since we didn't returned a cached
                            // value.
                            request.Features.Set<CachedResolutionFeature>(null);
                        }
                    }
                    else if (fromCache)
                    {
                        // Make sure the next attempt re-resolves location+category and sets refreshCache to true.
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

            return await _next.InvokeAsync(request, cancel).ConfigureAwait(false);
        }

        /// <summary>Constructs a locator invoker.</summary>
        internal LocatorInvoker(ILocatorPrx locator, Interceptors.LocatorOptions options, IInvoker next)
        {
            _locator = locator;
            _background = options.Background;
            _cacheMaxSize = options.CacheMaxSize;
            _cache = new(concurrencyLevel: 1, capacity: _cacheMaxSize + 1);
            _justRefreshedAge = options.JustRefreshedAge;
            _logger = options.LoggerFactory.CreateLogger("IceRpc");
            _ttl = options.Ttl;
            _next = next;

            // See Interceptors.Locator
            Debug.Assert(_locator.Endpoint != null && _locator.Endpoint.Transport != Transport.Loc);
            Debug.Assert(_ttl == Timeout.InfiniteTimeSpan || _justRefreshedAge < _ttl);
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
                        _logger.LogClearCacheEntry(location, category, entry.Endpoint, entry.AltEndpoints);
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
            if (endpoint?.Transport == Transport.Loc)
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
                    if (endpoint == null || endpoint.Transport == Transport.Loc)
                    {
                        ClearCache(location, category);
                    }
                }
            }

            if (endpoint != null)
            {
                if (resolved)
                {
                    _logger.LogResolved(location, category, endpoint, altEndpoints);
                }
                else
                {
                    _logger.LogFoundEntryInCache(location, category, endpoint, altEndpoints);
                }
            }
            else
            {
                _logger.LogCouldNotResolveEndpoint(location, category);
            }

            return (endpoint, altEndpoints, endpoint != null && !resolved);
        }

        private async Task<(Endpoint?, ImmutableList<Endpoint>)> ResolveWithLocatorAsync(
            string location,
            string? category,
            CancellationToken cancel)
        {
            _logger.LogResolving(location, category);

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
                    IServicePrx? proxy = null;

                    if (category == null)
                    {
                        try
                        {
                            proxy = await _locator.FindAdapterByIdAsync(
                                location,
                                cancel: CancellationToken.None).ConfigureAwait(false);
                        }
                        catch (AdapterNotFoundException)
                        {
                            // We treat AdapterNotFoundException just like a null return value.
                            proxy = null;
                        }
                    }
                    else
                    {
                        try
                        {
                            proxy = await _locator.FindObjectByIdAsync(
                                new Identity(location, category),
                                cancel: CancellationToken.None).ConfigureAwait(false);
                        }
                        catch (ObjectNotFoundException)
                        {
                            // We treat ObjectNotFoundException just like a null return value.
                            proxy = null;
                        }
                    }

                    ServicePrx? resolved = proxy?.Impl;

                    if (resolved != null &&
                        ((category == null && resolved.IsIndirect) ||
                          resolved.IsWellKnown ||
                          resolved.Protocol != Protocol.Ice1))
                    {
                        _logger.LogReceivedInvalidProxy(location, category, resolved);
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
                    _logger.LogResolveFailure(location, category, exception);
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
