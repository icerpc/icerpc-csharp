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

namespace IceRpc
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
        private readonly ConcurrentDictionary<string, (TimeSpan InsertionTime, IReadOnlyList<Endpoint> Endpoints)> _adapterIdCache =
            new();

        private readonly Dictionary<string, Task<IReadOnlyList<Endpoint>>> _adapterIdRequests = new();
        private readonly bool _background;
        private readonly ConcurrentDictionary<Identity, (TimeSpan InsertionTime, IReadOnlyList<Endpoint> Endpoints)> _identityCache =
            new();

        private readonly Dictionary<Identity, Task<IReadOnlyList<Endpoint>>> _identityRequests = new();

        private readonly ILocatorPrx _locator;

        // _mutex protects _adapterIdRequests and _identityRequests
        private readonly object _mutex = new();

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

        public ValueTask<(IReadOnlyList<Endpoint> Endpoints, TimeSpan EndpointsAge)> ResolveAsync(
            Endpoint endpoint,
            TimeSpan endpointsMaxAge,
            CancellationToken cancel)
        {
            if (endpoint.Transport != Transport.Loc)
            {
                throw new ArgumentException($"{nameof(endpoint)}'s transport is not loc", nameof(endpoint));
            }

            if (endpoint["category"] is string category)
            {
                return ResolveIdentityAsync(new Identity(endpoint.Host, category), endpointsMaxAge, cancel);
            }
            else
            {
                return ResolveAdapterIdAsync(endpoint.Host, endpointsMaxAge, cancel);
            }
        }

        private async ValueTask<(IReadOnlyList<Endpoint> Endpoints, TimeSpan EndpointsAge)> ResolveAdapterIdAsync(
            string adapterId,
            TimeSpan endpointsMaxAge,
            CancellationToken cancel)
        {
            if (adapterId.Length == 0)
            {
                throw new ArgumentException("invalid empty adapter ID", nameof(adapterId));
            }

            IReadOnlyList<Endpoint> endpoints = ImmutableArray<Endpoint>.Empty;
            TimeSpan endpointsAge = TimeSpan.Zero;
            bool expired = false;

            if (_ttl != TimeSpan.Zero)
            {
                (endpoints, endpointsAge) = GetResolveAdapterIdFromCache(adapterId);
                expired = CheckExpired(endpointsAge, _ttl);
            }

            if (endpoints.Count == 0 || (!_background && expired) || endpointsAge >= endpointsMaxAge)
            {
               endpoints = await ResolveAdapterIdAsync(adapterId, cancel).ConfigureAwait(false);
               endpointsAge = TimeSpan.Zero; // Not cached
            }
            else if (expired && _background)
            {
                // Endpoints are returned from the cache but endpoints MaxAge was reached, if backgrounds updates
                // are configured, we obtain new endpoints but continue using the stale endpoints to not block the
                // caller.
                _ = ResolveAdapterIdAsync(adapterId, cancel: default);
            }

            var logger = _locator.Communicator.LocatorClientLogger;
            if (logger.IsEnabled(LogLevel.Debug))
            {
                if (endpoints.Count > 0)
                {
                    if (endpointsAge == TimeSpan.Zero)
                    {
                        logger.LogResolvedAdapterId(adapterId, endpoints);
                    }
                    else
                    {
                        logger.LogFoundEntryForAdapterIdInLocatorCache(adapterId, endpoints);
                    }
                }
                else
                {
                    logger.LogCouldNotFindEndpointsForAdapterId(adapterId);
                }
            }

            return (endpoints, endpointsAge);
        }

        private async ValueTask<(IReadOnlyList<Endpoint> Endpoints, TimeSpan EndpointsAge)> ResolveIdentityAsync(
            Identity identity,
            TimeSpan endpointsMaxAge,
            CancellationToken cancel)
        {
            IReadOnlyList<Endpoint> endpoints = ImmutableArray<Endpoint>.Empty;
            TimeSpan wellKnownAge = TimeSpan.Zero;
            bool expired = false;

            // First, we check the cache.
            if (_ttl != TimeSpan.Zero)
            {
                (endpoints, wellKnownAge) = GetResolvedWellKnownProxyFromCache(identity);
                expired = CheckExpired(wellKnownAge, _ttl);
            }

            // If no endpoints are returned from the cache, or if the cache returned an expired endpoint and
            // background updates are disabled, or if the caller is requesting a more recent endpoint than the
            // one returned from the cache, we try to resolve the endpoint again.
            if (endpoints.Count == 0 || (!_background && expired) || wellKnownAge >= endpointsMaxAge)
            {
                endpoints = await ResolveIdentityAsync(identity, cancel).ConfigureAwait(false);
                wellKnownAge = TimeSpan.Zero; // Not cached
            }
            else if (_background && expired)
            {
                // Entry is returned from the cache but endpoints MaxAge was reached, if backgrounds updates are
                // configured, we make a new resolution to refresh the cache but use the stale info to not block
                // the caller.
                _ = ResolveIdentityAsync(identity, cancel: default);
            }

            if (endpoints.Count == 1 && endpoints[0].Transport == Transport.Loc)
            {
                if (endpoints[0].HasOptions)
                {
                    // We got back a new well-known proxy - can't use it
                    endpoints = ImmutableArray<Endpoint>.Empty;
                    ClearCache(identity);
                }
                else
                {
                    // We got back a loc endpoint with an adapter ID.
                    if (endpointsMaxAge > wellKnownAge)
                    {
                        // We always want endpoints that are fresher than the well-known cache entry.
                        endpointsMaxAge = wellKnownAge;
                    }

                    try
                    {
                        (endpoints, _) = await ResolveAdapterIdAsync(endpoints[0].Host,
                                                                     endpointsMaxAge,
                                                                     cancel).ConfigureAwait(false);
                    }
                    finally
                    {
                        if (endpoints.Count == 0 || endpoints[0].Transport == Transport.Loc)
                        {
                            endpoints = ImmutableArray<Endpoint>.Empty;
                            ClearCache(identity);
                        }
                    }
                }
            }

            var logger = _locator.Communicator.LocatorClientLogger;
            if (logger.IsEnabled(LogLevel.Debug))
            {
                if (endpoints.Count > 0)
                {
                    if (wellKnownAge == TimeSpan.Zero)
                    {
                        logger.LogResolvedWellKnownProxy(identity, endpoints);
                    }
                    else
                    {
                        logger.LogFoundEntryForWellKnownProxyInLocatorCache(identity, endpoints);
                    }
                }
                else
                {
                   logger.LogCouldNotFindEndpointsForWellKnownProxy(identity);
                }
            }

            return (endpoints, wellKnownAge);
        }

        private static bool CheckExpired(TimeSpan age, TimeSpan maxAge) =>
            maxAge != Timeout.InfiniteTimeSpan && age > maxAge;

        private void ClearCache(string adapterId)
        {
            if (_adapterIdCache.TryRemove(adapterId, out (TimeSpan _, IReadOnlyList<Endpoint> Endpoints) entry))
            {
                if (_locator.Communicator.LocatorClientLogger.IsEnabled(LogLevel.Trace))
                {
                    _locator.Communicator.LocatorClientLogger.LogClearAdapterIdEndpoints(adapterId, entry.Endpoints);
                }
            }
        }

        private void ClearCache(Identity identity)
        {
            if (_identityCache.TryRemove(
                    identity,
                    out (TimeSpan _, IReadOnlyList<Endpoint> Endpoints) entry))
            {
                if (entry.Endpoints[0].Transport == Transport.Loc)
                {
                    string adapterId = entry.Endpoints[0].Host;

                    if (_locator.Communicator.LocatorClientLogger.IsEnabled(LogLevel.Trace))
                    {
                        _locator.Communicator.LocatorClientLogger.LogClearWellKnownProxyWithoutEndpoints(
                            identity,
                            adapterId);
                    }

                    ClearCache(adapterId);
                }
                else
                {
                    if (_locator.Communicator.LocatorClientLogger.IsEnabled(LogLevel.Trace))
                    {
                        _locator.Communicator.LocatorClientLogger.LogClearWellKnownProxyEndpoints(
                            identity,
                            entry.Endpoints);
                    }
                }
            }
        }

        private (IReadOnlyList<Endpoint> Endpoints, TimeSpan EndpointsAge) GetResolveAdapterIdFromCache(string adapterId)
        {
            if (_adapterIdCache.TryGetValue(
                adapterId,
                out (TimeSpan InsertionTime, IReadOnlyList<Endpoint> Endpoints) entry))
            {
                return (entry.Endpoints, Time.Elapsed - entry.InsertionTime);
            }
            else
            {
                return (ImmutableArray<Endpoint>.Empty, TimeSpan.Zero);
            }
        }

        private (IReadOnlyList<Endpoint> Endpoints, TimeSpan LocationAge) GetResolvedWellKnownProxyFromCache(
            Identity identity)
        {
            if (_identityCache.TryGetValue(
                    identity,
                    out (TimeSpan InsertionTime, IReadOnlyList<Endpoint> Endpoints) entry))
            {
                return (entry.Endpoints, Time.Elapsed - entry.InsertionTime);
            }
            else
            {
                return (ImmutableArray<Endpoint>.Empty, TimeSpan.Zero);
            }
        }

        private async Task<IReadOnlyList<Endpoint>> ResolveAdapterIdAsync(string adapterId, CancellationToken cancel)
        {
            if (_locator.Communicator.LocatorClientLogger.IsEnabled(LogLevel.Debug))
            {
                _locator.Communicator.LocatorClientLogger.LogResolvingAdapterId(adapterId);
            }

            Task<IReadOnlyList<Endpoint>>? task;
            lock (_mutex)
            {
                if (!_adapterIdRequests.TryGetValue(adapterId, out task))
                {
                    // If there is no request in progress for this adapter ID, we invoke one and cache the request to
                    // prevent concurrent identical requests. It's removed once the response is received.
                    task = PerformResolveAdapterIdAsync(adapterId);
                    if (!task.IsCompleted)
                    {
                        // If PerformResolveAdapterIdAsync completed, don't add the task (it would leak since
                        // PerformResolveAdapterIdAsync is responsible for removing it).
                        // Since PerformResolveAdapterIdAsync locks _mutex in its finally block, the only way it can be
                        // completed now is if completed synchronously.
                        _adapterIdRequests.Add(adapterId, task);
                    }
                }
            }

            return await task.WaitAsync(cancel).ConfigureAwait(false);

            async Task<IReadOnlyList<Endpoint>> PerformResolveAdapterIdAsync(string adapterId)
            {
                Debug.Assert(adapterId.Length > 0);

                try
                {
                    IServicePrx? proxy = null;
                    try
                    {
                        proxy = await _locator.FindAdapterByIdAsync(
                            adapterId,
                            cancel: CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (AdapterNotFoundException)
                    {
                        // We treat AdapterNotFoundException just like a null return value.
                        proxy = null;
                    }

                    ServicePrx? resolved = proxy?.Impl;

                    if (resolved != null && (resolved.IsIndirect || resolved.Protocol != Protocol.Ice1))
                    {
                        if (_locator.Communicator.LocatorClientLogger.IsEnabled(LogLevel.Debug))
                        {
                            _locator.Communicator.LocatorClientLogger.LogInvalidProxyResolvingAdapterId(
                                adapterId,
                                resolved);
                        }
                        resolved = null;
                    }

                    IReadOnlyList<Endpoint> endpoints = resolved?.Endpoints ?? ImmutableList<Endpoint>.Empty;

                    if (endpoints.Count == 0)
                    {
                        ClearCache(adapterId);
                        return endpoints;
                    }
                    else
                    {
                        _adapterIdCache[adapterId] = (Time.Elapsed, endpoints);
                        return endpoints;
                    }
                }
                catch (Exception exception)
                {
                    if (_locator.Communicator.LocatorClientLogger.IsEnabled(LogLevel.Error))
                    {
                        _locator.Communicator.LocatorClientLogger.LogResolveAdapterIdFailure(adapterId, exception);
                    }
                    throw;
                }
                finally
                {
                    lock (_mutex)
                    {
                        _adapterIdRequests.Remove(adapterId);
                    }
                }
            }
        }

        private async Task<IReadOnlyList<Endpoint>> ResolveIdentityAsync(
            Identity identity,
            CancellationToken cancel)
        {
            if (_locator.Communicator.LocatorClientLogger.IsEnabled(LogLevel.Debug))
            {
                _locator.Communicator.LocatorClientLogger.LogResolvingWellKnownProxy(identity);
            }

            Task<IReadOnlyList<Endpoint>>? task;
            lock (_mutex)
            {
                if (!_identityRequests.TryGetValue(identity, out task))
                {
                    // If there's no locator request in progress for this object, we make one and cache it to prevent
                    // making too many requests on the locator. It's removed once the locator response is received.
                    task = PerformResolveIdentityAsync(identity);
                    if (!task.IsCompleted)
                    {
                        // If PerformResolveIdentityAsync completed, don't add the task (it would leak since
                        // PerformResolveIdentityAsync is responsible for removing it).
                        _identityRequests.Add(identity, task);
                    }
                }
            }

            return await task.WaitAsync(cancel).ConfigureAwait(false);

            async Task<IReadOnlyList<Endpoint>> PerformResolveIdentityAsync(Identity identity)
            {
                try
                {
                    IServicePrx? obj = null;
                    try
                    {
                        obj = await _locator.FindObjectByIdAsync(
                            identity,
                            cancel: CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (ObjectNotFoundException)
                    {
                        // We treat ObjectNotFoundException just like a null return value.
                        obj = null;
                    }

                    ServicePrx? resolved = obj?.Impl;

                    if (resolved != null && (resolved.IsWellKnown || resolved.Protocol != Protocol.Ice1))
                    {
                        if (_locator.Communicator.LocatorClientLogger.IsEnabled(LogLevel.Debug))
                        {
                            _locator.Communicator.LocatorClientLogger.LogInvalidProxyResolvingProxy(identity, resolved);
                        }
                        resolved = null;
                    }

                    IReadOnlyList<Endpoint> endpoints = resolved?.Endpoints ?? ImmutableList<Endpoint>.Empty;

                    if (endpoints.Count == 0)
                    {
                        ClearCache(identity);
                        return endpoints;
                    }
                    else
                    {
                        TimeSpan resolvedTime = Time.Elapsed;
                        _identityCache[identity] = (resolvedTime, endpoints);
                        return endpoints;
                    }
                }
                catch (Exception exception)
                {
                    if (_locator.Communicator.LocatorClientLogger.IsEnabled(LogLevel.Error))
                    {
                        _locator.Communicator.LocatorClientLogger.LogResolveWellKnownProxyEndpointsFailure(
                            identity,
                            exception);
                    }
                    throw;
                }
                finally
                {
                    lock (_mutex)
                    {
                        _identityRequests.Remove(identity);
                    }
                }
            }
        }
    }
}
