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

        /// <summary>After ttl, a cache entry is considered stale. The default value is InfiniteTimeSpan, meaning the
        /// cache entries never become stale.</summary>
        public TimeSpan Ttl { get; set; } = Timeout.InfiniteTimeSpan;
    }

    /// <summary>Implements <see cref="ILocationResolver"/> using a <see cref="ILocatorPrx"/>.</summary>
    public sealed class LocatorClient : ILocationResolver
    {
        private readonly ConcurrentDictionary<(string Location, string? Category), (TimeSpan InsertionTime, IReadOnlyList<Endpoint> Endpoints)> _cache =
            new();

        private readonly bool _background;
        private readonly ILocatorPrx _locator;

        // _mutex protects _requests
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
            _ttl = options.Ttl;
        }

        /// <summary>Constructs a locator client using the default options.</summary>
        /// <param name="locator">The locator proxy.</param>
        public LocatorClient(ILocatorPrx locator)
            : this(locator, new LocatorClientOptions())
        {
        }

        public async ValueTask<(IReadOnlyList<Endpoint> Endpoints, TimeSpan EndpointsAge)> ResolveAsync(
            Endpoint endpoint,
            TimeSpan endpointsMaxAge,
            CancellationToken cancel)
        {
            if (endpoint.Transport != Transport.Loc)
            {
                throw new ArgumentException($"{nameof(endpoint)}'s transport is not loc", nameof(endpoint));
            }

            if (endpoint.Protocol != Protocol.Ice1)
            {
                // LocatorClient works only for ice1 endpoints.
                return (ImmutableList<Endpoint>.Empty, TimeSpan.Zero);
            }

            string location = endpoint.Host;
            if (location.Length == 0)
            {
                throw new ArgumentException("endpoint.Host cannot be empty", nameof(endpoint));
            }

            string? category = endpoint["category"];

            IReadOnlyList<Endpoint> endpoints = ImmutableArray<Endpoint>.Empty;
            TimeSpan endpointsAge = TimeSpan.Zero;
            bool expired = false;

            if (_ttl != TimeSpan.Zero &&
                _cache.TryGetValue((location, category),
                                    out (TimeSpan InsertionTime, IReadOnlyList<Endpoint> Endpoints) entry))
            {
                endpoints = entry.Endpoints;
                endpointsAge = Time.Elapsed - entry.InsertionTime;
                expired = _ttl != Timeout.InfiniteTimeSpan && endpointsAge > _ttl;
            }

            // Timeout.InfiniteTimeSpan == -1 so does not work well in comparisons.
            if (endpoints.Count == 0 ||
                (!_background && expired) ||
                (endpointsMaxAge != Timeout.InfiniteTimeSpan && endpointsAge >= endpointsMaxAge))
            {
               endpoints = await ResolveAsync(location, category, cancel).ConfigureAwait(false);
               endpointsAge = TimeSpan.Zero; // Fresh endpoints that are not coming from the cache.
            }
            else if (expired && _background)
            {
                // Endpoints are returned from the cache but endpointsMaxAge was reached, if backgrounds updates
                // are configured, we obtain new endpoints but continue using the stale endpoints to not block the
                // caller.
                _ = ResolveAsync(location, category, cancel: default);
            }

            // A well-known proxy resolution can return a loc endpoint, but not another well-known proxy loc endpoint.
            if (endpoints.Count == 1 && endpoints[0].Transport == Transport.Loc)
            {
                Debug.Assert(category != null);

                if (endpointsMaxAge != Timeout.InfiniteTimeSpan && endpointsMaxAge > endpointsAge)
                {
                    // We always want endpoints that are fresher than the well-known cache entry.
                    endpointsMaxAge = endpointsAge;
                }

                try
                {
                    // Resolves adapter ID recursively, by checking first the cache.
                    (endpoints, _) = await ResolveAsync(endpoints[0], endpointsMaxAge, cancel).ConfigureAwait(false);
                }
                finally
                {
                    // endpoints can hold a loc endpoint only when an exception is thrown.
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
                    if (endpointsAge == TimeSpan.Zero)
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

            return (endpoints, endpointsAge);
        }

        private void ClearCache(string location, string? category)
        {
            if (_cache.TryRemove((location, category), out (TimeSpan _, IReadOnlyList<Endpoint> Endpoints) entry))
            {
                if (_locator.Communicator.LocatorClientLogger.IsEnabled(LogLevel.Trace))
                {
                    _locator.Communicator.LocatorClientLogger.LogClearCacheEntry(location, category, entry.Endpoints);
                }
            }
        }

        private async Task<IReadOnlyList<Endpoint>> ResolveAsync(
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
                    task = PerformResolveAsync();
                    if (!task.IsCompleted)
                    {
                        // If PerformResolveAsync completed, don't add the task (it would leak since PerformResolveAsync
                        // is responsible for removing it).
                        // Since PerformResolveAsync locks _mutex in its finally block, the only way it can be completed
                        // now is if completed synchronously.
                        _requests.Add((location, category), task);
                    }
                }
            }

            return await task.WaitAsync(cancel).ConfigureAwait(false);

            async Task<IReadOnlyList<Endpoint>> PerformResolveAsync()
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
                        return endpoints;
                    }
                    else
                    {
                        _cache[(location, category)] = (Time.Elapsed, endpoints);
                        return endpoints;
                    }
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
