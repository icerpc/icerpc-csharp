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
    /// <summary>The locator info class caches information specific to a given locator proxy. The communicator holds a
    /// locator info instance per locator proxy set either with Ice.Default.Locator or the proxy's Locator property. It
    /// caches the locator registry proxy and keeps track of requests to the locator to prevent multiple concurrent
    /// identical requests.</summary>
    internal sealed class LocatorInfo
    {
        internal ILocatorPrx Locator { get; }
        private ILogger Logger { get; }

        private static readonly IEqualityComparer<(Location, Protocol)> _locationComparer = new LocationComparer();

        private readonly bool _background;

        private readonly ConcurrentDictionary<(Location, Protocol), (TimeSpan InsertionTime, EndpointList Endpoints)> _locationCache =
            new(_locationComparer);

        private readonly Dictionary<(Location, Protocol), Task<EndpointList>> _locationRequests =
            new(_locationComparer);

        // _mutex protects _locationRequests and _wellKnownProxyRequests
        private readonly object _mutex = new();

        private readonly ConcurrentDictionary<(Identity, string, Protocol), (TimeSpan InsertionTime, EndpointList Endpoints, Location Location)> _wellKnownProxyCache =
            new();

        private readonly Dictionary<(Identity, string, Protocol), Task<(EndpointList, Location)>> _wellKnownProxyRequests =
            new();

        internal LocatorInfo(ILocatorPrx locator, bool background)
        {
            Locator = locator;
            _background = background;
            Logger = Locator.Communicator.Logger;
        }

        internal void ClearCache(ObjectPrx proxy)
        {
            Debug.Assert(proxy.IsIndirect);

            if (proxy.IsWellKnown)
            {
                if (_wellKnownProxyCache.TryRemove(
                    (proxy.Identity, proxy.Facet, proxy.Protocol),
                    out (TimeSpan _, EndpointList Endpoints, Location Location) entry))
                {
                    if (entry.Endpoints.Count > 0)
                    {
                        if (Logger.IsEnabled(LogLevel.Information))
                        {
                            Logger.LogClearWellKnownProxyEndpoints(proxy, proxy.Endpoints);
                        }
                    }
                    else
                    {
                        Debug.Assert(entry.Location.Count > 0);
                        if (Logger.IsEnabled(LogLevel.Information))
                        {
                            Logger.LogClearWellKnownProxyWithoutEndpoints(proxy, entry.Location);
                        }
                        ClearCache(entry.Location, proxy.Protocol);
                    }
                }
            }
            else
            {
                ClearCache(proxy.Location, proxy.Protocol);
            }
        }

        /// <summary>Resolves an indirect proxy using the locator proxy or cache.</summary>
        internal async ValueTask<(EndpointList Endpoints, TimeSpan EndpointsAge)> ResolveIndirectProxyAsync(
            ObjectPrx proxy,
            TimeSpan endpointsMaxAge,
            CancellationToken cancel)
        {
            Debug.Assert(proxy.IsIndirect);

            EndpointList endpoints = ImmutableArray<Endpoint>.Empty;
            TimeSpan wellKnownLocationAge = TimeSpan.Zero;

            Location location = proxy.Location;
            if (proxy.IsWellKnown)
            {
                // First, we check the cache.
                if (proxy.LocatorCacheTimeout != TimeSpan.Zero)
                {
                    (endpoints, location, wellKnownLocationAge) = GetResolvedWellKnownProxyFromCache(proxy);
                }
                bool expired = CheckExpired(wellKnownLocationAge, proxy.LocatorCacheTimeout);
                // If no endpoints are returned from the cache, or if the cache returned an expired endpoint and
                // background updates are disabled, or if the caller is requesting a more recent endpoint than the
                // one returned from the cache, we try to resolve the endpoint again.
                if ((endpoints.Count == 0 && location.Count == 0) ||
                    (!_background && expired) ||
                    wellKnownLocationAge >= endpointsMaxAge)
                {
                    (endpoints, location) = await ResolveWellKnownProxyAsync(proxy, cancel).ConfigureAwait(false);
                    wellKnownLocationAge = TimeSpan.Zero; // Not cached
                }
                else if (_background && expired)
                {
                    // Entry is returned from the cache but endpoints MaxAge was reached, if backgrounds updates are
                    // configured, we make a new resolution to refresh the cache but use the stale info to not block
                    // the caller.
                    _ = ResolveWellKnownProxyAsync(proxy, cancel: default);
                }
            }

            TimeSpan endpointsAge = TimeSpan.Zero;
            if (location.Count > 0)
            {
                Debug.Assert(endpoints.Count == 0);

                if (proxy.LocatorCacheTimeout != TimeSpan.Zero)
                {
                    (endpoints, endpointsAge) = GetResolvedLocationFromCache(location, proxy.Protocol);
                }

                bool expired = CheckExpired(endpointsAge, proxy.LocatorCacheTimeout);
                if (endpoints.Count == 0 ||
                    (!_background && expired) ||
                    endpointsAge >= endpointsMaxAge ||
                    (proxy.IsWellKnown && wellKnownLocationAge <= endpointsAge))
                {
                    try
                    {
                        endpoints = await ResolveLocationAsync(location,
                                                               proxy.Protocol,
                                                               proxy.Communicator,
                                                               cancel).ConfigureAwait(false);
                        endpointsAge = TimeSpan.Zero; // Not cached
                    }
                    finally
                    {
                        // If we can't resolve the location we clear the resolved well-known proxy from the cache.
                        if (endpoints.Count == 0 && proxy.IsWellKnown)
                        {
                            ClearCache(proxy);
                        }
                    }
                }
                else if (expired && _background)
                {
                    // Endpoints are returned from the cache but endpoints MaxAge was reached, if backgrounds updates
                    // are configured, we obtain new endpoints but continue using the stale endpoints to not block the
                    // caller.
                    _ = ResolveLocationAsync(location, proxy.Protocol, proxy.Communicator, cancel: default);
                }
            }

            if (Logger.IsEnabled(LogLevel.Information))
            {
                if (endpoints.Count > 0)
                {
                    if (proxy.IsWellKnown)
                    {
                        if (wellKnownLocationAge == TimeSpan.Zero)
                        {
                            Logger.LogResolvedWellKnownProxy(proxy, endpoints);
                        }
                        else
                        {
                            Logger.LogFoundEntryForWellKnownProxyInLocatorCache(proxy, endpoints);
                        }
                    }
                    else
                    {
                        if (endpointsAge == TimeSpan.Zero)
                        {
                            Logger.LogResolvedLocation(location, proxy.Protocol, endpoints);
                        }
                        else
                        {
                            Logger.LogFoundEntryForLocationInLocatorCache(location, proxy.Protocol, endpoints);
                        }
                    }
                }
                else
                {
                    if (proxy.Location.Count > 0)
                    {
                        Logger.LogCouldNotFindEndpointsForLocation(proxy.Location);
                    }
                    else
                    {
                        Logger.LogCouldNotFindEndpointsForWellKnownProxy(proxy);
                    }
                }
            }

            return (endpoints, proxy.IsWellKnown ? wellKnownLocationAge : endpointsAge);
        }

        private static bool CheckExpired(TimeSpan age, TimeSpan maxAge) =>
            maxAge != Timeout.InfiniteTimeSpan && age > maxAge;

        private void ClearCache(Location location, Protocol protocol)
        {
            if (_locationCache.TryRemove((location, protocol), out (TimeSpan _, EndpointList Endpoints) entry))
            {
                if (Logger.IsEnabled(LogLevel.Information))
                {
                    Logger.LogClearLocationEndpoints(location.ToLocationString(), protocol, entry.Endpoints);
                }
            }
        }

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
            ObjectPrx proxy)
        {
            if (_wellKnownProxyCache.TryGetValue(
                    (proxy.Identity, proxy.Facet, proxy.Protocol),
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
            Communicator communicator,
            CancellationToken cancel)
        {
            if (Logger.IsEnabled(LogLevel.Information))
            {
                Logger.LogResolvingLocation(location);
            }

            Task<EndpointList>? task;
            lock (_mutex)
            {
                if (!_locationRequests.TryGetValue((location, protocol), out task))
                {
                    // If there is no request in progress for this location, we invoke one and cache the request to
                    // prevent concurrent identical requests. It's removed once the response is received.
                    task = PerformResolveLocationAsync(location, protocol, communicator);
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

            async Task<EndpointList> PerformResolveLocationAsync(
                Location location,
                Protocol protocol,
                Communicator communicator)
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
                            proxy = await Locator.FindAdapterByIdAsync(
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
                            if (Logger.IsEnabled(LogLevel.Error))
                            {
                                Logger.LogInvalidProxyResolvingLocation(location.ToLocationString(), resolved);
                            }
                            resolved = null;
                        }

                        endpoints = resolved?.Endpoints ?? endpoints;
                    }
                    else
                    {
                        EndpointData[] dataArray;

                        // This will throw OperationNotExistException if it's an old Locator, and that's fine.
                        dataArray = await Locator.ResolveLocationAsync(
                            location,
                            cancel: CancellationToken.None).ConfigureAwait(false);

                        if (dataArray.Length > 0)
                        {
                            endpoints = dataArray.ToEndpointList(communicator);
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
                    if (Logger.IsEnabled(LogLevel.Error))
                    {
                        Logger.LogResolveLocationFailure(location, exception);
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
            ObjectPrx proxy,
            CancellationToken cancel)
        {
            if (Logger.IsEnabled(LogLevel.Information))
            {
                Logger.LogResolvingWellKnownProxy(proxy);
            }

            Task<(EndpointList, Location)>? task;
            lock (_mutex)
            {
                if (!_wellKnownProxyRequests.TryGetValue((proxy.Identity, proxy.Facet, proxy.Protocol),
                                                         out task))
                {
                    // If there's no locator request in progress for this object, we make one and cache it to prevent
                    // making too many requests on the locator. It's removed once the locator response is received.
                    task = PerformResolveWellKnownProxyAsync(proxy);
                    if (!task.IsCompleted)
                    {
                        // If PerformGetObjectProxyAsync completed, don't add the task (it would leak since
                        // PerformGetObjectProxyAsync is responsible for removing it).
                        _wellKnownProxyRequests.Add((proxy.Identity, proxy.Facet, proxy.Protocol), task);
                    }
                }
            }

            return await task.WaitAsync(cancel).ConfigureAwait(false);

            async Task<(EndpointList, Location)> PerformResolveWellKnownProxyAsync(ObjectPrx proxy)
            {
                try
                {
                    EndpointList endpoints;
                    Location location;

                    if (proxy.Protocol == Protocol.Ice1)
                    {
                        IObjectPrx? obj = null;
                        try
                        {
                            obj = await Locator.FindObjectByIdAsync(
                                proxy.Identity,
                                proxy.Facet,
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
                            if (Logger.IsEnabled(LogLevel.Information))
                            {
                                Logger.LogInvalidProxyResolvingProxy(proxy, resolved);
                            }
                            resolved = null;
                        }

                        endpoints = resolved?.Endpoints ?? ImmutableArray<Endpoint>.Empty;
                        location = resolved?.Location ?? ImmutableArray<string>.Empty;
                    }
                    else
                    {
                        EndpointData[] dataArray;
                        (dataArray, location) = await Locator.ResolveWellKnownProxyAsync(
                            proxy.Identity,
                            proxy.Facet,
                            cancel: CancellationToken.None).ConfigureAwait(false);

                        if (dataArray.Length > 0)
                        {
                            endpoints = dataArray.ToEndpointList(proxy.Communicator);
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
                        ClearCache(proxy);
                        return (endpoints, location);
                    }
                    else
                    {
                        Debug.Assert(endpoints.Count == 0 || location.Count == 0);
                        TimeSpan resolvedTime = Time.Elapsed;
                        _wellKnownProxyCache[(proxy.Identity, proxy.Facet, proxy.Protocol)] =
                            (resolvedTime, endpoints, location);
                        return (endpoints, location);
                    }
                }
                catch (Exception exception)
                {
                    if (Logger.IsEnabled(LogLevel.Error))
                    {
                        Logger.LogResolveWellKnownProxyEndpointsFailure(proxy, exception);
                    }
                    throw;
                }
                finally
                {
                    lock (_mutex)
                    {
                        _wellKnownProxyRequests.Remove((proxy.Identity, proxy.Facet, proxy.Protocol));
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
