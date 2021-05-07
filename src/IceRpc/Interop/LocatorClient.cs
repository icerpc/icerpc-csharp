// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Interop
{
    /// <summary>An options class for configuring a <see cref="LocatorClient"/>.</summary>
    public sealed class LocatorClientOptions
    {
        /// <summary>When true, if the lookup finds a stale cache entry, it returns the stale entry's endpoint(s)
        /// and executes a call "in the background" to refresh this entry. The default is false, meaning the lookup
        /// does not return stale values.</summary>
        public bool Background { get; set; }

        /// <summary>The maximum size of the cache. Must be 0 (meaning no cache) or greater.</summary>
        public int CacheMaxSize
        {
            get => _cacheMaxSize;
            set => _cacheMaxSize = value >= 0 ? value :
                throw new ArgumentException($"{nameof(CacheMaxSize)} must be positive", nameof(value));
        }

        /// <summary>When a cache entry's age is <c>JustRefreshedAge</c> or less, it's considered just refreshed and
        /// won't be updated even when the caller requests a refresh.</summary>
        public TimeSpan JustRefreshedAge { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>The logger factory used to create the IceRpc logger.</summary>
        public ILoggerFactory LoggerFactory { get; set; } = Runtime.DefaultLoggerFactory;

        /// <summary>After ttl, a cache entry is considered stale. The default value is InfiniteTimeSpan, meaning
        /// the cache entries never become stale.</summary>
        public TimeSpan Ttl { get; set; } = Timeout.InfiniteTimeSpan;

        private int _cacheMaxSize = 100;
    }

    /// <summary>A location resolver that provides the implementation of the Locator interceptor.</summary>
    public sealed class LocatorClient : ILocationResolver, IIdentityResolver
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

        private readonly Dictionary<(string Location, string? Category), Task<(Endpoint?, ImmutableList<Endpoint>)>> _requests =
            new();

        private readonly TimeSpan _ttl;

        /// <summary>Constructs a locator client.</summary>
        /// <param name="locator">The locator proxy.</param>
        /// <param name="options">Options to configure this locator client.</param>
        public LocatorClient(ILocatorPrx locator, LocatorClientOptions options)
        {
            _locator = locator;
            _background = options.Background;
            _cacheMaxSize = options.CacheMaxSize;
            _cache = new(concurrencyLevel: 1, capacity: _cacheMaxSize + 1);
            _justRefreshedAge = options.JustRefreshedAge;
            _logger = options.LoggerFactory.CreateLogger("IceRpc");
            _ttl = options.Ttl;

            if (_ttl != Timeout.InfiniteTimeSpan && _justRefreshedAge >= _ttl)
            {
                throw new ArgumentException("JustRefreshedAge must be smaller than Ttl", nameof(options));
            }
        }

        /// <summary>Constructs a locator client with defailt options.</summary>
        /// <param name="locator">The locator proxy.</param>
        public LocatorClient(ILocatorPrx locator)
            : this(locator, new())
        {
        }

        /// <inherit-doc/>
        public ValueTask<(Endpoint? Endpoint, ImmutableList<Endpoint> AltEndpoints)> ResolveAsync(
            Endpoint locEndpoint,
            bool refreshCache,
            CancellationToken cancel) =>
            locEndpoint.Transport == Transport.Loc ?
                ResolveAsync(locEndpoint.Host, category: null, refreshCache, cancel) :
                new((null, ImmutableList<Endpoint>.Empty));

        /// <inherit-doc/>
        public ValueTask<(Endpoint? Endpoint, ImmutableList<Endpoint> AltEndpoints)> ResolveAsync(
            Identity identity,
            bool refreshCache,
            CancellationToken cancel) => ResolveAsync(identity.Name, identity.Category, refreshCache, cancel);

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

        /// <summary>Provides the implementation of the public ResolveAsync methods.</summary>
        private async ValueTask<(Endpoint?, ImmutableList<Endpoint>)> ResolveAsync(
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
                    (endpoint, altEndpoints) = await ResolveAsync(endpoint!,
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
            return (endpoint, altEndpoints);
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
    }
}
