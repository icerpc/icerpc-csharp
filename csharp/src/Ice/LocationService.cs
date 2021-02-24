// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroC.Ice
{
    /// <summary>An options class for configuring a <see cref="LocationService"/>.</summary>
    public sealed class LocationServiceOptions
    {
        /// <summary>When true, if a Resolve method finds a stale cache entry, it returns the stale entry's endpoint(s)
        /// and executes a call "in the background" to refresh this entry. The default is false, meaning the Resolve
        /// methods do not return stale values.</summary>
        public bool Background { get; set; }

        /// <summary>After ttl, a cache entry is considered stale. The default value is InfiniteTimeSpan, meaning the
        /// cache entries never become stale.</summary>
        public TimeSpan Ttl { get; set; } = Timeout.InfiniteTimeSpan;
    }

    /// <summary>The default implementation of <see cref="ILocationService"/>. It relies on a <see cref="ILocatorPrx"/>.
    /// </summary>
    public sealed class LocationService : ILocationService
    {
        private readonly bool _background;

        private readonly ConcurrentDictionary<string, (TimeSpan InsertionTime, IReadOnlyList<Endpoint> Endpoints)> _locationCache =
            new();

        private readonly Dictionary<string, Task<IReadOnlyList<Endpoint>>> _locationRequests = new();

        private readonly ILocatorPrx _locator;

        // _mutex protects _locationRequests and _wellKnownProxyRequests
        private readonly object _mutex = new();

        private readonly TimeSpan _ttl;

        private readonly ConcurrentDictionary<Identity, (TimeSpan InsertionTime, IReadOnlyList<Endpoint> Endpoints, string Location)> _wellKnownProxyCache =
            new();

        private readonly Dictionary<Identity, Task<(IReadOnlyList<Endpoint>, string)>> _wellKnownProxyRequests = new();

        /// <summary>Constructs a location service.</summary>
        /// <param name="locator">The locator proxy.</param>
        /// <param name="options">Options to configure this location service.See <see cref="LocationServiceOptions"/>.
        /// </param>
        public LocationService(ILocatorPrx locator, LocationServiceOptions options)
        {
            _locator = locator;
            _background = options.Background;
            _ttl = options.Ttl;
        }

        /// <summary>Constructs a location service using the default options.</summary>
        /// <param name="locator">The locator proxy.</param>
        public LocationService(ILocatorPrx locator)
            : this(locator, new LocationServiceOptions())
        {
        }

        public async ValueTask<(IReadOnlyList<Endpoint> Endpoints, TimeSpan EndpointsAge)> ResolveLocationAsync(
            string location,
            TimeSpan endpointsMaxAge,
            CancellationToken cancel)
        {
            if (location.Length == 0)
            {
                throw new ArgumentException("invalid empty location", nameof(location));
            }

            IReadOnlyList<Endpoint> endpoints = ImmutableArray<Endpoint>.Empty;
            TimeSpan endpointsAge = TimeSpan.Zero;
            bool expired = false;

            if (_ttl != TimeSpan.Zero)
            {
                (endpoints, endpointsAge) = GetResolvedLocationFromCache(location);
                expired = CheckExpired(endpointsAge, _ttl);
            }

            if (endpoints.Count == 0 || (!_background && expired) || endpointsAge >= endpointsMaxAge)
            {
               endpoints = await ResolveLocationAsync(location, cancel).ConfigureAwait(false);
               endpointsAge = TimeSpan.Zero; // Not cached
            }
            else if (expired && _background)
            {
                // Endpoints are returned from the cache but endpoints MaxAge was reached, if backgrounds updates
                // are configured, we obtain new endpoints but continue using the stale endpoints to not block the
                // caller.
                _ = ResolveLocationAsync(location, cancel: default);
            }

            var logger = _locator.Communicator.LocationLogger;
            if (logger.IsEnabled(LogLevel.Debug))
            {
                if (endpoints.Count > 0)
                {
                    if (endpointsAge == TimeSpan.Zero)
                    {
                        logger.LogResolvedLocation(location, endpoints);
                    }
                    else
                    {
                        logger.LogFoundEntryForLocationInLocatorCache(location, endpoints);
                    }
                }
                else
                {
                    logger.LogCouldNotFindEndpointsForLocation(location);
                }
            }

            return (endpoints, endpointsAge);
        }

        public async ValueTask<(IReadOnlyList<Endpoint> Endpoints, TimeSpan EndpointsAge)> ResolveWellKnownProxyAsync(
            Identity identity,
            TimeSpan endpointsMaxAge,
            CancellationToken cancel)
        {
            IReadOnlyList<Endpoint> endpoints = ImmutableArray<Endpoint>.Empty;
            TimeSpan wellKnownAge = TimeSpan.Zero;
            string location = "";
            bool expired = false;

            // First, we check the cache.
            if (_ttl != TimeSpan.Zero)
            {
                (endpoints, location, wellKnownAge) = GetResolvedWellKnownProxyFromCache(identity);
                expired = CheckExpired(wellKnownAge, _ttl);
            }

            // If no endpoints are returned from the cache, or if the cache returned an expired endpoint and
            // background updates are disabled, or if the caller is requesting a more recent endpoint than the
            // one returned from the cache, we try to resolve the endpoint again.
            if ((endpoints.Count == 0 && location.Length == 0) ||
                (!_background && expired) ||
                wellKnownAge >= endpointsMaxAge)
            {
                (endpoints, location) =
                    await ResolveWellKnownProxyAsync(identity, cancel).ConfigureAwait(false);
                wellKnownAge = TimeSpan.Zero; // Not cached
            }
            else if (_background && expired)
            {
                // Entry is returned from the cache but endpoints MaxAge was reached, if backgrounds updates are
                // configured, we make a new resolution to refresh the cache but use the stale info to not block
                // the caller.
                _ = ResolveWellKnownProxyAsync(identity, cancel: default);
            }

            if (location.Length > 0)
            {
                Debug.Assert(endpoints.Count == 0);

                if (endpointsMaxAge > wellKnownAge)
                {
                    // We always want location endpoints that are fresher than the well-known cache entry.
                    endpointsMaxAge = wellKnownAge;
                }

                try
                {
                    (endpoints, _) =
                        await ResolveLocationAsync(location, endpointsMaxAge, cancel).ConfigureAwait(false);
                }
                finally
                {
                    // If we can't resolve the location we clear the resolved well-known proxy from the cache.
                    if (endpoints.Count == 0)
                    {
                        ClearCache(identity);
                    }
                }
            }

            var logger = _locator.Communicator.LocationLogger;
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

        private void ClearCache(string location)
        {
            if (_locationCache.TryRemove(location, out (TimeSpan _, IReadOnlyList<Endpoint> Endpoints) entry))
            {
                if (_locator.Communicator.LocationLogger.IsEnabled(LogLevel.Trace))
                {
                    _locator.Communicator.LocationLogger.LogClearLocationEndpoints(location, entry.Endpoints);
                }
            }
        }

        private void ClearCache(Identity identity)
        {
            if (_wellKnownProxyCache.TryRemove(
                    identity,
                    out (TimeSpan _, IReadOnlyList<Endpoint> Endpoints, string Location) entry))
            {
                if (entry.Endpoints.Count > 0)
                {
                    if (_locator.Communicator.LocationLogger.IsEnabled(LogLevel.Trace))
                    {
                        _locator.Communicator.LocationLogger.LogClearWellKnownProxyEndpoints(
                            identity,
                            entry.Endpoints);
                    }
                }
                else
                {
                    Debug.Assert(entry.Location.Length > 0);

                    if (_locator.Communicator.LocationLogger.IsEnabled(LogLevel.Trace))
                    {
                        _locator.Communicator.LocationLogger.LogClearWellKnownProxyWithoutEndpoints(
                            identity,
                            entry.Location);
                    }

                    ClearCache(entry.Location);
                }
            }
        }

        private (IReadOnlyList<Endpoint> Endpoints, TimeSpan EndpointsAge) GetResolvedLocationFromCache(string location)
        {
            if (_locationCache.TryGetValue(
                location,
                out (TimeSpan InsertionTime, IReadOnlyList<Endpoint> Endpoints) entry))
            {
                return (entry.Endpoints, Time.Elapsed - entry.InsertionTime);
            }
            else
            {
                return (ImmutableArray<Endpoint>.Empty, TimeSpan.Zero);
            }
        }

        private (IReadOnlyList<Endpoint> Endpoints, string Location, TimeSpan LocationAge) GetResolvedWellKnownProxyFromCache(
            Identity identity)
        {
            if (_wellKnownProxyCache.TryGetValue(
                    identity,
                    out (TimeSpan InsertionTime, IReadOnlyList<Endpoint> Endpoints, string Location) entry))
            {
                return (entry.Endpoints, entry.Location, Time.Elapsed - entry.InsertionTime);
            }
            else
            {
                return (ImmutableArray<Endpoint>.Empty, "", TimeSpan.Zero);
            }
        }

        private async Task<IReadOnlyList<Endpoint>> ResolveLocationAsync(string location, CancellationToken cancel)
        {
            if (_locator.Communicator.LocationLogger.IsEnabled(LogLevel.Debug))
            {
                _locator.Communicator.LocationLogger.LogResolvingLocation(location);
            }

            Task<IReadOnlyList<Endpoint>>? task;
            lock (_mutex)
            {
                if (!_locationRequests.TryGetValue(location, out task))
                {
                    // If there is no request in progress for this location, we invoke one and cache the request to
                    // prevent concurrent identical requests. It's removed once the response is received.
                    task = PerformResolveLocationAsync(location);
                    if (!task.IsCompleted)
                    {
                        // If PerformResolveLocationAsync completed, don't add the task (it would leak since
                        // PerformResolveLocationAsync is responsible for removing it).
                        // Since PerformResolveLocationAsync locks _mutex in its finally block, the only way it can be
                        // completed now is if completed synchronously.
                        _locationRequests.Add(location, task);
                    }
                }
            }

            return await task.WaitAsync(cancel).ConfigureAwait(false);

            async Task<IReadOnlyList<Endpoint>> PerformResolveLocationAsync(string location)
            {
                Debug.Assert(location.Length > 0);

                try
                {
                    IReadOnlyList<Endpoint> endpoints = ImmutableArray<Endpoint>.Empty;
                    IObjectPrx? proxy = null;
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

                    ObjectPrx? resolved = proxy?.Impl;

                    if (resolved != null && (resolved.Endpoints.Count == 0 || resolved.Protocol != Protocol.Ice1))
                    {
                        if (_locator.Communicator.LocationLogger.IsEnabled(LogLevel.Debug))
                        {
                            _locator.Communicator.LocationLogger.LogInvalidProxyResolvingLocation(
                                location,
                                resolved);
                        }
                        resolved = null;
                    }

                    endpoints = resolved?.Endpoints ?? endpoints;

                    if (endpoints.Count == 0)
                    {
                        ClearCache(location);
                        return endpoints;
                    }
                    else
                    {
                        // Cache the resolved location
                        _locationCache[location] = (Time.Elapsed, endpoints);
                        return endpoints;
                    }
                }
                catch (Exception exception)
                {
                    if (_locator.Communicator.LocationLogger.IsEnabled(LogLevel.Error))
                    {
                        _locator.Communicator.LocationLogger.LogResolveLocationFailure(location, exception);
                    }
                    throw;
                }
                finally
                {
                    lock (_mutex)
                    {
                        _locationRequests.Remove(location);
                    }
                }
            }
        }

        private async Task<(IReadOnlyList<Endpoint>, string)> ResolveWellKnownProxyAsync(
            Identity identity,
            CancellationToken cancel)
        {
            if (_locator.Communicator.LocationLogger.IsEnabled(LogLevel.Debug))
            {
                _locator.Communicator.LocationLogger.LogResolvingWellKnownProxy(identity);
            }

            Task<(IReadOnlyList<Endpoint>, string)>? task;
            lock (_mutex)
            {
                if (!_wellKnownProxyRequests.TryGetValue(identity, out task))
                {
                    // If there's no locator request in progress for this object, we make one and cache it to prevent
                    // making too many requests on the locator. It's removed once the locator response is received.
                    task = PerformResolveWellKnownProxyAsync(identity);
                    if (!task.IsCompleted)
                    {
                        // If PerformGetObjectProxyAsync completed, don't add the task (it would leak since
                        // PerformGetObjectProxyAsync is responsible for removing it).
                        _wellKnownProxyRequests.Add(identity, task);
                    }
                }
            }

            return await task.WaitAsync(cancel).ConfigureAwait(false);

            async Task<(IReadOnlyList<Endpoint>, string)> PerformResolveWellKnownProxyAsync(Identity identity)
            {
                try
                {
                    IReadOnlyList<Endpoint> endpoints = ImmutableList<Endpoint>.Empty;
                    string location = "";

                    IObjectPrx? obj = null;
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

                    ObjectPrx? resolved = obj?.Impl;

                    if (resolved != null && (resolved.IsWellKnown || resolved.Protocol != Protocol.Ice1))
                    {
                        if (_locator.Communicator.LocationLogger.IsEnabled(LogLevel.Debug))
                        {
                            _locator.Communicator.LocationLogger.LogInvalidProxyResolvingProxy(identity, resolved);
                        }
                        resolved = null;
                    }

                    if (resolved != null)
                    {
                        endpoints = resolved.Endpoints;
                        if (resolved.Location.Count > 0)
                        {
                            location = resolved.Location[0];
                        }
                    }

                    if (endpoints.Count == 0 && location.Length == 0)
                    {
                        ClearCache(identity);
                        return (endpoints, location);
                    }
                    else
                    {
                        Debug.Assert(endpoints.Count == 0 || location.Length == 0);
                        TimeSpan resolvedTime = Time.Elapsed;
                        _wellKnownProxyCache[identity] = (resolvedTime, endpoints, location);
                        return (endpoints, location);
                    }
                }
                catch (Exception exception)
                {
                    if (_locator.Communicator.LocationLogger.IsEnabled(LogLevel.Error))
                    {
                        _locator.Communicator.LocationLogger.LogResolveWellKnownProxyEndpointsFailure(
                            identity,
                            exception);
                    }
                    throw;
                }
                finally
                {
                    lock (_mutex)
                    {
                        _wellKnownProxyRequests.Remove(identity);
                    }
                }
            }
        }
    }
}
