// Copyright (c) ZeroC, Inc. All rights reserved.

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
        /// <summary>When true, if ResolveAsync finds a stale cache entry, it returns the stale entry's endpoint(s)
        /// and executes a call "in the background" to refresh this entry. The default is false, meaning ResolveAsync
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

        /// <summary>After ttl, a cache entry is considered stale. The default value is InfiniteTimeSpan, meaning the
        /// cache entries never become stale.</summary>
        public TimeSpan Ttl { get; set; } = Timeout.InfiniteTimeSpan;

        private int _cacheMaxSize = 100;
    }

    /// <summary>Implements <see cref="ILocationResolver"/> using a <see cref="ILocatorPrx"/>.</summary>
    public sealed class LocatorClient : ILocationResolver
    {
        private bool HasCache => _ttl != TimeSpan.Zero && _cacheMaxSize > 0;
        private readonly bool _background;
        private readonly ConcurrentDictionary<(string Location, string? Category), (TimeSpan InsertionTime, IReadOnlyList<Endpoint> Endpoints, LinkedListNode<(string Location, string? Category)> Node)> _cache;

        // The keys in _cache. The first entries correspond to the most recently added cache entries.
        private readonly LinkedList<(string Location, string? Category)> _cacheKeys = new();

        private readonly int _cacheMaxSize;

        private readonly TimeSpan _justRefreshedAge;

        private readonly ILocatorPrx _locator;

        // _mutex protects _cacheKeys, _requests and updates to _cache
        private readonly object _mutex = new();

        private readonly Dictionary<(string Location, string? Category), Task<IReadOnlyList<Endpoint>>> _requests =
            new();

        private readonly TimeSpan _ttl;

        /// <summary>Constructs a locator client.</summary>
        /// <param name="locator">The locator proxy.</param>
        /// <param name="options">Options to configure this locator client. See <see cref="LocatorClientOptions"/>.
        /// </param>
        public LocatorClient(ILocatorPrx locator, LocatorClientOptions options)
        {
            _locator = locator;
            _background = options.Background;
            _cacheMaxSize = options.CacheMaxSize;
            _cache = new(concurrencyLevel: 1, capacity: _cacheMaxSize + 1);
            _justRefreshedAge = options.JustRefreshedAge;
            _ttl = options.Ttl;

            if (_ttl != Timeout.InfiniteTimeSpan && _justRefreshedAge >= _ttl)
            {
                throw new ArgumentException("JustRefreshedAge must be smaller than Ttl", nameof(options));
            }
        }

        /// <summary>Constructs a locator client using the default options.</summary>
        /// <param name="locator">The locator proxy.</param>
        public LocatorClient(ILocatorPrx locator)
            : this(locator, new LocatorClientOptions())
        {
        }

        public async ValueTask<IReadOnlyList<Endpoint>> ResolveAsync(
            Endpoint endpoint,
            bool refreshCache,
            CancellationToken cancel)
        {
            if (endpoint.Transport != Transport.Loc)
            {
                throw new ArgumentException($"{nameof(endpoint)}'s transport is not loc", nameof(endpoint));
            }

            if (endpoint.Protocol != Protocol.Ice1)
            {
                // LocatorClient works only for ice1 endpoints.
                return ImmutableList<Endpoint>.Empty;
            }

            string location = endpoint.Host;
            if (location.Length == 0)
            {
                throw new ArgumentException("endpoint.Host cannot be empty", nameof(endpoint));
            }

            string? category = endpoint["category"];

            IReadOnlyList<Endpoint> endpoints = ImmutableList<Endpoint>.Empty;
            bool expired = false;
            bool justRefreshed = false;
            bool resolved = false;

            if (HasCache)
            {
                lock (_mutex)
                {
                    if(_cache.TryGetValue(
                        (location, category),
                        out (TimeSpan InsertionTime, IReadOnlyList<Endpoint> Endpoints, LinkedListNode<(string, string?)> _) entry))
                    {
                        endpoints = entry.Endpoints;
                        TimeSpan cacheEntryAge = Time.Elapsed - entry.InsertionTime;
                        expired = _ttl != Timeout.InfiniteTimeSpan && cacheEntryAge > _ttl;
                        justRefreshed = cacheEntryAge <= _justRefreshedAge;
                    }
                }
            }

            if (endpoints.Count == 0 || (!_background && expired) || (refreshCache && !justRefreshed))
            {
               endpoints = await ResolveWithLocatorAsync(location, category, cancel).ConfigureAwait(false);
               resolved = true;
            }
            else if (_background && expired)
            {
                // We retrieved expired endpoints from the cache, so we launch a refresh in the background.
                _ = ResolveWithLocatorAsync(location, category, cancel: default);
            }

            // A well-known proxy resolution can return a loc endpoint, but not another well-known proxy loc endpoint.
            if (endpoints.Count == 1 && endpoints[0].Transport == Transport.Loc)
            {
                Debug.Assert(category != null);

                try
                {
                    // Resolves adapter ID recursively, by checking first the cache. If we resolved the endpoint, we
                    // request a cache refresh for the adapter.
                    endpoints =
                        await ResolveAsync(endpoints[0], refreshCache || resolved, cancel).ConfigureAwait(false);
                }
                finally
                {
                    // When the second resolution fails, we clear the cache entry for the initial successful resolution,
                    // since the overall resolution is a failure.
                    // endpoints below can hold a loc endpoint only when an exception is thrown.
                    if (endpoints.Count == 0 || (endpoints.Count == 1 && endpoints[0].Transport == Transport.Loc))
                    {
                        ClearCache(location, category);
                    }
                }
            }

            var logger = _locator.Communicator.LocatorClientLogger;
            if (logger.IsEnabled(LogLevel.Debug))
            {
                if (endpoints.Count > 0)
                {
                    if (resolved)
                    {
                        logger.LogResolved(location, category, endpoints);
                    }
                    else
                    {
                        logger.LogFoundEntryInCache(location, category, endpoints);
                    }
                }
                else
                {
                    logger.LogCouldNotResolveEndpoint(location, category);
                }
            }

            return endpoints;
        }

        private void ClearCache(string location, string? category)
        {
            if (HasCache)
            {
                lock (_mutex)
                {
                    if (_cache.TryRemove(
                        (location, category),
                        out (TimeSpan _, IReadOnlyList<Endpoint> Endpoints, LinkedListNode<(string, string?)> Node) entry))
                    {
                        _cacheKeys.Remove(entry.Node);

                        if (_locator.Communicator.LocatorClientLogger.IsEnabled(LogLevel.Trace))
                        {
                            _locator.Communicator.LocatorClientLogger.LogClearCacheEntry(location,
                                                                                         category,
                                                                                         entry.Endpoints);
                        }
                    }
                }
            }
        }

        private async Task<IReadOnlyList<Endpoint>> ResolveWithLocatorAsync(
            string location,
            string? category,
            CancellationToken cancel)
        {
            if (_locator.Communicator.LocatorClientLogger.IsEnabled(LogLevel.Debug))
            {
                _locator.Communicator.LocatorClientLogger.LogResolving(location, category);
            }

            Task<IReadOnlyList<Endpoint>>? task;
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

            async Task<IReadOnlyList<Endpoint>> PerformResolveWithLocatorAsync()
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
                        if (_locator.Communicator.LocatorClientLogger.IsEnabled(LogLevel.Debug))
                        {
                            _locator.Communicator.LocatorClientLogger.LogReceivedInvalidProxy(
                                location,
                                category,
                                resolved);
                        }
                        resolved = null;
                    }

                    IReadOnlyList<Endpoint> endpoints = resolved?.Endpoints ?? ImmutableList<Endpoint>.Empty;

                    if (endpoints.Count == 0)
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

                            _cache[(location, category)] =
                                (Time.Elapsed, endpoints, _cacheKeys.AddFirst((location, category)));

                            if (_cacheKeys.Count == _cacheMaxSize + 1)
                            {
                                // drop last (oldest) entry
                                (string lastLocation, string? lastCategory) = _cacheKeys.Last!.Value;
                                ClearCache(lastLocation, lastCategory);

                                Debug.Assert(_cacheKeys.Count == _cacheMaxSize); // removed the last entry
                            }
                        }
                    }
                    return endpoints;
                }
                catch (Exception exception)
                {
                    if (_locator.Communicator.LocatorClientLogger.IsEnabled(LogLevel.Error))
                    {
                        _locator.Communicator.LocatorClientLogger.LogResolveFailure(location, category, exception);
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
    }
}
