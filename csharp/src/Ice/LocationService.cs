// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using EndpointList = System.Collections.Generic.IReadOnlyList<ZeroC.Ice.Endpoint>;
using Location = System.Collections.Generic.IReadOnlyList<string>;

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
        private static readonly IEqualityComparer<(Location, Protocol)> _locationComparer = new LocationComparer();

        private readonly bool _background;

        private readonly ConcurrentDictionary<(Location, Protocol), (TimeSpan InsertionTime, EndpointList Endpoints)> _locationCache =
            new(_locationComparer);

        private readonly Dictionary<(Location, Protocol), Task<EndpointList>> _locationRequests =
            new(_locationComparer);

        private readonly ILocatorPrx _locator;

        // _mutex protects _locationRequests and _wellKnownProxyRequests
        private readonly object _mutex = new();

        private readonly TimeSpan _ttl;

        private readonly ConcurrentDictionary<(Identity, string, Protocol), (TimeSpan InsertionTime, EndpointList Endpoints, Location Location)> _wellKnownProxyCache =
            new();

        private readonly Dictionary<(Identity, string, Protocol), Task<(EndpointList, Location)>> _wellKnownProxyRequests =
            new();

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

        /// <inheritdoc/>
        public void ClearCache(Location location, Protocol protocol)
        {
            if (_locationCache.TryRemove((location, protocol), out (TimeSpan _, EndpointList Endpoints) entry))
            {
                if (_locator.Communicator.LocationLogger.IsEnabled(LogLevel.Trace))
                {
                    _locator.Communicator.LocationLogger.LogClearLocationEndpoints(location[0], protocol, entry.Endpoints);
                }
            }
        }

        /// <inheritdoc/>
        public void ClearCache(Identity identity, string facet, Protocol protocol)
        {
            if (_wellKnownProxyCache.TryRemove(
                    (identity, facet, protocol),
                    out (TimeSpan _, EndpointList Endpoints, Location Location) entry))
            {
                if (entry.Endpoints.Count > 0)
                {
                    if (_locator.Communicator.LocationLogger.IsEnabled(LogLevel.Trace))
                    {
                        _locator.Communicator.LocationLogger.LogClearWellKnownProxyEndpoints(
                            identity,
                            facet,
                            protocol,
                            entry.Endpoints);
                    }
                }
                else
                {
                    Debug.Assert(entry.Location.Count > 0);

                    if (_locator.Communicator.LocationLogger.IsEnabled(LogLevel.Trace))
                    {
                        _locator.Communicator.LocationLogger.LogClearWellKnownProxyWithoutEndpoints(
                            identity,
                            facet,
                            protocol,
                            entry.Location);
                    }

                    ClearCache(entry.Location, protocol);
                }
            }
        }

        public async ValueTask<(EndpointList Endpoints, TimeSpan EndpointsAge)> ResolveLocationAsync(
            Location location,
            Protocol protocol,
            TimeSpan endpointsMaxAge,
            CancellationToken cancel)
        {
            if (location.Count == 0)
            {
                throw new ArgumentException("invalid empty location", nameof(location));
            }

            EndpointList endpoints = ImmutableArray<Endpoint>.Empty;
            TimeSpan endpointsAge = TimeSpan.Zero;
            bool expired = false;

            if (_ttl != TimeSpan.Zero)
            {
                (endpoints, endpointsAge) = GetResolvedLocationFromCache(location, protocol);
                expired = CheckExpired(endpointsAge, _ttl);
            }

            if (endpoints.Count == 0 || (!_background && expired) || endpointsAge >= endpointsMaxAge)
            {
               endpoints = await ResolveLocationAsync(location, protocol, cancel).ConfigureAwait(false);
               endpointsAge = TimeSpan.Zero; // Not cached
            }
            else if (expired && _background)
            {
                // Endpoints are returned from the cache but endpoints MaxAge was reached, if backgrounds updates
                // are configured, we obtain new endpoints but continue using the stale endpoints to not block the
                // caller.
                _ = ResolveLocationAsync(location, protocol, cancel: default);
            }

            var logger = _locator.Communicator.LocationLogger;
            if (logger.IsEnabled(LogLevel.Debug))
            {
                if (endpoints.Count > 0)
                {
                    if (endpointsAge == TimeSpan.Zero)
                    {
                        logger.LogResolvedLocation(location, protocol, endpoints);
                    }
                    else
                    {
                        logger.LogFoundEntryForLocationInLocatorCache(location, protocol, endpoints);
                    }
                }
                else
                {
                    logger.LogCouldNotFindEndpointsForLocation(location);
                }
            }

            return (endpoints, endpointsAge);
        }

        public async ValueTask<(EndpointList Endpoints, TimeSpan EndpointsAge)> ResolveWellKnownProxyAsync(
            Identity identity,
            string facet,
            Protocol protocol,
            TimeSpan endpointsMaxAge,
            CancellationToken cancel)
        {
            EndpointList endpoints = ImmutableArray<Endpoint>.Empty;
            TimeSpan wellKnownAge = TimeSpan.Zero;
            Location location = ImmutableList<string>.Empty;
            bool expired = false;

            // First, we check the cache.
            if (_ttl != TimeSpan.Zero)
            {
                (endpoints, location, wellKnownAge) = GetResolvedWellKnownProxyFromCache(identity, facet, protocol);
                expired = CheckExpired(wellKnownAge, _ttl);
            }

            // If no endpoints are returned from the cache, or if the cache returned an expired endpoint and
            // background updates are disabled, or if the caller is requesting a more recent endpoint than the
            // one returned from the cache, we try to resolve the endpoint again.
            if ((endpoints.Count == 0 && location.Count == 0) ||
                (!_background && expired) ||
                wellKnownAge >= endpointsMaxAge)
            {
                (endpoints, location) =
                    await ResolveWellKnownProxyAsync(identity, facet, protocol, cancel).ConfigureAwait(false);
                wellKnownAge = TimeSpan.Zero; // Not cached
            }
            else if (_background && expired)
            {
                // Entry is returned from the cache but endpoints MaxAge was reached, if backgrounds updates are
                // configured, we make a new resolution to refresh the cache but use the stale info to not block
                // the caller.
                _ = ResolveWellKnownProxyAsync(identity, facet, protocol, cancel: default);
            }

            if (location.Count > 0)
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
                        await ResolveLocationAsync(location, protocol, endpointsMaxAge, cancel).ConfigureAwait(false);
                }
                finally
                {
                    // If we can't resolve the location we clear the resolved well-known proxy from the cache.
                    if (endpoints.Count == 0)
                    {
                        ClearCache(identity, facet, protocol);
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
                        logger.LogResolvedWellKnownProxy(identity, facet, protocol, endpoints);
                    }
                    else
                    {
                        logger.LogFoundEntryForWellKnownProxyInLocatorCache(identity, facet, protocol, endpoints);
                    }
                }
                else
                {
                   logger.LogCouldNotFindEndpointsForWellKnownProxy(identity, facet, protocol);
                }
            }

            return (endpoints, wellKnownAge);
        }

        private static bool CheckExpired(TimeSpan age, TimeSpan maxAge) =>
            maxAge != Timeout.InfiniteTimeSpan && age > maxAge;

        private (EndpointList Endpoints, TimeSpan EndpointsAge) GetResolvedLocationFromCache(
            Location location,
            Protocol protocol)
        {
            if (_locationCache.TryGetValue(
                (location, protocol),
                out (TimeSpan InsertionTime, EndpointList Endpoints) entry))
            {
                return (entry.Endpoints, Time.Elapsed - entry.InsertionTime);
            }
            else
            {
                return (ImmutableArray<Endpoint>.Empty, TimeSpan.Zero);
            }
        }

        private (EndpointList Endpoints, Location Location, TimeSpan LocationAge) GetResolvedWellKnownProxyFromCache(
            Identity identity,
            string facet,
            Protocol protocol)
        {
            if (_wellKnownProxyCache.TryGetValue(
                    (identity, facet, protocol),
                    out (TimeSpan InsertionTime, EndpointList Endpoints, Location Location) entry))
            {
                return (entry.Endpoints, entry.Location, Time.Elapsed - entry.InsertionTime);
            }
            else
            {
                return (ImmutableArray<Endpoint>.Empty, ImmutableArray<string>.Empty, TimeSpan.Zero);
            }
        }

        private async Task<EndpointList> ResolveLocationAsync(
            Location location,
            Protocol protocol,
            CancellationToken cancel)
        {
            if (_locator.Communicator.LocationLogger.IsEnabled(LogLevel.Debug))
            {
                _locator.Communicator.LocationLogger.LogResolvingLocation(location);
            }

            Task<EndpointList>? task;
            lock (_mutex)
            {
                if (!_locationRequests.TryGetValue((location, protocol), out task))
                {
                    // If there is no request in progress for this location, we invoke one and cache the request to
                    // prevent concurrent identical requests. It's removed once the response is received.
                    task = PerformResolveLocationAsync(location, protocol);
                    if (!task.IsCompleted)
                    {
                        // If PerformResolveLocationAsync completed, don't add the task (it would leak since
                        // PerformResolveLocationAsync is responsible for removing it).
                        // Since PerformResolveLocationAsync locks _mutex in its finally block, the only way it can be
                        // completed now is if completed synchronously.
                        _locationRequests.Add((location, protocol), task);
                    }
                }
            }

            return await task.WaitAsync(cancel).ConfigureAwait(false);

            async Task<EndpointList> PerformResolveLocationAsync(Location location, Protocol protocol)
            {
                Debug.Assert(location.Count > 0);

                try
                {
                    EndpointList endpoints = ImmutableArray<Endpoint>.Empty;

                    if (protocol == Protocol.Ice1)
                    {
                        IObjectPrx? proxy = null;
                        try
                        {
                            proxy = await _locator.FindAdapterByIdAsync(
                                location[0],
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
                                    location.ToLocationString(),
                                    resolved);
                            }
                            resolved = null;
                        }

                        endpoints = resolved?.Endpoints ?? endpoints;
                    }
                    else
                    {
                        EndpointData[] dataArray;

                        // This will throw OperationNotExistException if it's an old Locator, and that's fine.
                        dataArray = await _locator.ResolveLocationAsync(
                            location,
                            cancel: CancellationToken.None).ConfigureAwait(false);

                        if (dataArray.Length > 0)
                        {
                            endpoints = dataArray.ToEndpointList(_locator.Communicator);
                        }
                    }

                    if (endpoints.Count == 0)
                    {
                        ClearCache(location, protocol);
                        return endpoints;
                    }
                    else
                    {
                        // Cache the resolved location
                        _locationCache[(location, protocol)] = (Time.Elapsed, endpoints);
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
                        _locationRequests.Remove((location, protocol));
                    }
                }
            }
        }

        private async Task<(EndpointList, Location)> ResolveWellKnownProxyAsync(
            Identity identity,
            string facet,
            Protocol protocol,
            CancellationToken cancel)
        {
            if (_locator.Communicator.LocationLogger.IsEnabled(LogLevel.Debug))
            {
                _locator.Communicator.LocationLogger.LogResolvingWellKnownProxy(identity, facet, protocol);
            }

            Task<(EndpointList, Location)>? task;
            lock (_mutex)
            {
                if (!_wellKnownProxyRequests.TryGetValue((identity, facet, protocol),
                                                         out task))
                {
                    // If there's no locator request in progress for this object, we make one and cache it to prevent
                    // making too many requests on the locator. It's removed once the locator response is received.
                    task = PerformResolveWellKnownProxyAsync(identity, facet, protocol);
                    if (!task.IsCompleted)
                    {
                        // If PerformGetObjectProxyAsync completed, don't add the task (it would leak since
                        // PerformGetObjectProxyAsync is responsible for removing it).
                        _wellKnownProxyRequests.Add((identity, facet, protocol), task);
                    }
                }
            }

            return await task.WaitAsync(cancel).ConfigureAwait(false);

            async Task<(EndpointList, Location)> PerformResolveWellKnownProxyAsync(
                Identity identity,
                string facet,
                Protocol protocol)
            {
                try
                {
                    EndpointList endpoints;
                    Location location;

                    if (protocol == Protocol.Ice1)
                    {
                        IObjectPrx? obj = null;
                        try
                        {
                            obj = await _locator.FindObjectByIdAsync(
                                identity,
                                facet,
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
                                _locator.Communicator.LocationLogger.LogInvalidProxyResolvingProxy(identity, facet, resolved);
                            }
                            resolved = null;
                        }

                        endpoints = resolved?.Endpoints ?? ImmutableArray<Endpoint>.Empty;
                        location = resolved?.Location ?? ImmutableArray<string>.Empty;
                    }
                    else
                    {
                        EndpointData[] dataArray;
                        (dataArray, location) = await _locator.ResolveWellKnownProxyAsync(
                            identity,
                            facet,
                            cancel: CancellationToken.None).ConfigureAwait(false);

                        if (dataArray.Length > 0)
                        {
                            endpoints = dataArray.ToEndpointList(_locator.Communicator);
                            location = ImmutableArray<string>.Empty; // always wipe-out / ignore location
                        }
                        else
                        {
                            endpoints = ImmutableArray<Endpoint>.Empty;
                            // and keep returned location
                        }
                    }

                    if (endpoints.Count == 0 && location.Count == 0)
                    {
                        ClearCache(identity, facet, protocol);
                        return (endpoints, location);
                    }
                    else
                    {
                        Debug.Assert(endpoints.Count == 0 || location.Count == 0);
                        TimeSpan resolvedTime = Time.Elapsed;
                        _wellKnownProxyCache[(identity, facet, protocol)] =
                            (resolvedTime, endpoints, location);
                        return (endpoints, location);
                    }
                }
                catch (Exception exception)
                {
                    if (_locator.Communicator.LocationLogger.IsEnabled(LogLevel.Error))
                    {
                        _locator.Communicator.LocationLogger.LogResolveWellKnownProxyEndpointsFailure(
                            identity,
                            facet,
                            protocol,
                            exception);
                    }
                    throw;
                }
                finally
                {
                    lock (_mutex)
                    {
                        _wellKnownProxyRequests.Remove((identity, facet, protocol));
                    }
                }
            }
        }

        private sealed class LocationComparer : IEqualityComparer<(Location Location, Protocol Protocol)>
        {
            public bool Equals(
                (Location Location, Protocol Protocol) lhs,
                (Location Location, Protocol Protocol) rhs) =>
                lhs.Location.SequenceEqual(rhs.Location) && lhs.Protocol == rhs.Protocol;

            public int GetHashCode((Location Location, Protocol Protocol) obj) =>
                HashCode.Combine(obj.Location.GetSequenceHashCode(), obj.Protocol);
        }
    }
}
