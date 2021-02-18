// Copyright (c) ZeroC, Inc. All rights reserved.

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
    public sealed class LocationResolverOptions
    {
        public bool Background { get; set; }

        public TimeSpan Ttl { get; set; } = Timeout.InfiniteTimeSpan;
    }

    /// <summary>The default implementation of ILocationResolver, using a locator proxy.</summary>
    public sealed class LocationResolver : ILocationResolver
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

        /// <summary>Constructs a location resolver.</summary>
        /// <param name="locator">The locator proxy.</param>
        /// <param name="options">Options to configure this location resolver.See <see cref="LocationResolverOptions"/>.
        /// </param>
        public LocationResolver(ILocatorPrx locator, LocationResolverOptions options)
        {
            _locator = locator;
            _background = options.Background;
            _ttl = options.Ttl;
        }

        /// <summary>Constructs a location resolver using the default options.</summary>
        /// <param name="locator">The locator proxy.</param>
        public LocationResolver(ILocatorPrx locator)
            : this(locator, new LocationResolverOptions())
        {
        }

        /// <inheritdoc/>
        public void ClearCache(ObjectPrx proxy)
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
                        if (proxy.Communicator.TraceLevels.Locator >= 2)
                        {
                            Trace("removed well-known proxy with endpoints from locator cache",
                                  proxy,
                                  entry.Endpoints);
                        }
                    }
                    else
                    {
                        Debug.Assert(entry.Location.Count > 0);
                        if (proxy.Communicator.TraceLevels.Locator >= 2)
                        {
                            Trace("removed well-known proxy without endpoints from locator cache",
                                  proxy,
                                  entry.Location);
                        }

                        ClearCache(entry.Location, proxy.Protocol, proxy.Communicator);
                    }
                }
            }
            else
            {
                ClearCache(proxy.Location, proxy.Protocol, proxy.Communicator);
            }
        }

        /// <inheritdoc/>
        public async ValueTask<(EndpointList Endpoints, TimeSpan EndpointsAge)> ResolveIndirectProxyAsync(
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
                if (_ttl != TimeSpan.Zero)
                {
                    (endpoints, location, wellKnownLocationAge) = GetResolvedWellKnownProxyFromCache(proxy);
                }
                bool expired = CheckExpired(wellKnownLocationAge, _ttl);
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

                if (_ttl != TimeSpan.Zero)
                {
                    (endpoints, endpointsAge) = GetResolvedLocationFromCache(location, proxy.Protocol);
                }

                bool expired = CheckExpired(endpointsAge, _ttl);
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

            if (proxy.Communicator.TraceLevels.Locator >= 1)
            {
                if (endpoints.Count > 0)
                {
                    if (proxy.IsWellKnown)
                    {
                        Trace(wellKnownLocationAge != TimeSpan.Zero ?
                                $"found entry for well-known proxy in locator cache" :
                                $"resolved well-known proxy using locator, adding to locator cache",
                              proxy,
                              endpoints);
                    }
                    else
                    {
                        Trace(endpointsAge != TimeSpan.Zero ?
                                $"found entry for location in locator cache" :
                                $"resolved location using locator, adding to locator cache",
                              location,
                              proxy.Protocol,
                              endpoints,
                              proxy.Communicator);
                    }
                }
                else
                {
                    Communicator communicator = proxy.Communicator;
                    var sb = new System.Text.StringBuilder();
                    sb.Append("could not find endpoint(s) for ");
                    if (proxy.Location.Count > 0)
                    {
                        sb.Append("location ");
                        sb.Append(proxy.Location.ToLocationString());
                    }
                    else
                    {
                        sb.Append("well-known proxy ");
                        sb.Append(proxy);
                    }
                    communicator.Logger.Trace(TraceLevels.LocatorCategory, sb.ToString());
                }
            }

            return (endpoints, proxy.IsWellKnown ? wellKnownLocationAge : endpointsAge);
        }

        private static bool CheckExpired(TimeSpan age, TimeSpan maxAge) =>
            maxAge != Timeout.InfiniteTimeSpan && age > maxAge;

        private static void Trace(
            string msg,
            Location location,
            Protocol protocol,
            EndpointList endpoints,
            Communicator communicator)
        {
            var sb = new System.Text.StringBuilder(msg);
            sb.Append("\nlocation = ");
            sb.Append(location.ToLocationString());
            sb.Append("\nprotocol = ");
            sb.Append(protocol.GetName());
            sb.Append("\nendpoints = ");
            sb.AppendEndpointList(endpoints);
            communicator.Logger.Trace(TraceLevels.LocatorCategory, sb.ToString());
        }

        private static void Trace(string msg, ObjectPrx wellKnownProxy, EndpointList endpoints)
        {
            var sb = new System.Text.StringBuilder(msg);
            sb.Append("\nwell-known proxy = ");
            sb.Append(wellKnownProxy);
            sb.Append("\nendpoints = ");
            sb.AppendEndpointList(endpoints);
            wellKnownProxy.Communicator.Logger.Trace(TraceLevels.LocatorCategory, sb.ToString());
        }

        private static void Trace(string msg, ObjectPrx wellKnownProxy, Location location)
        {
            var sb = new System.Text.StringBuilder(msg);
            sb.Append("\nwell-known proxy = ");
            sb.Append(wellKnownProxy);
            sb.Append("\nlocation = ");
            sb.Append(location.ToLocationString());
            wellKnownProxy.Communicator.Logger.Trace(TraceLevels.LocatorCategory, sb.ToString());
        }

        private static void TraceInvalid(Location location, ObjectPrx invalidObjectPrx)
        {
            var sb = new System.Text.StringBuilder("locator returned an invalid proxy when resolving location ");
            sb.Append(location.ToLocationString());
            sb.Append("\n received = ");
            sb.Append(invalidObjectPrx);
            invalidObjectPrx.Communicator.Logger.Trace(TraceLevels.LocatorCategory, sb.ToString());
        }

        private static void TraceInvalid(ObjectPrx proxy, ObjectPrx invalidObjectPrx)
        {
            var sb = new System.Text.StringBuilder("locator returned an invalid proxy when resolving ");
            sb.Append(proxy);
            sb.Append("\n received = ");
            sb.Append(invalidObjectPrx);
            proxy.Communicator.Logger.Trace(TraceLevels.LocatorCategory, sb.ToString());
        }

        private void ClearCache(Location location, Protocol protocol, Communicator communicator)
        {
            if (_locationCache.TryRemove((location, protocol), out (TimeSpan _, EndpointList Endpoints) entry))
            {
                if (communicator.TraceLevels.Locator >= 2)
                {
                    Trace("removed endpoints for location from locator cache",
                          location,
                          protocol,
                          entry.Endpoints,
                          communicator);
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
            if (communicator.TraceLevels.Locator > 0)
            {
                communicator.Logger.Trace(TraceLevels.LocatorCategory,
                    $"resolving location\nlocation = {location.ToLocationString()}");
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
                            if (communicator.TraceLevels.Locator >= 1)
                            {
                                TraceInvalid(location, resolved);
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
                            endpoints = dataArray.ToEndpointList(communicator);
                        }
                    }

                    if (endpoints.Count == 0)
                    {
                        ClearCache(location, protocol, communicator);
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
                    if (communicator.TraceLevels.Locator > 0)
                    {
                        communicator.Logger.Trace(
                            TraceLevels.LocatorCategory,
                            @$"could not contact the locator to resolve location `{location.ToLocationString()
                                }'\nreason = {exception}");
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
            if (proxy.Communicator.TraceLevels.Locator > 0)
            {
                proxy.Communicator.Logger.Trace(TraceLevels.LocatorCategory,
                    $"searching for well-known object\nwell-known proxy = {proxy}");
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
                            obj = await _locator.FindObjectByIdAsync(
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
                            if (proxy.Communicator.TraceLevels.Locator >= 1)
                            {
                                TraceInvalid(proxy, resolved);
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
                    if (proxy.Communicator.TraceLevels.Locator > 0)
                    {
                        proxy.Communicator.Logger.Trace(
                            TraceLevels.LocatorCategory,
                            @$"could not contact the locator to retrieve endpoints for well-known proxy `{proxy
                                }'\nreason = {exception}");
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
