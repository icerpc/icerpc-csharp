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
    /// <summary>.</summary>
    internal interface ILocationResolver
    {
        ValueTask<(Proxy? Proxy, bool FromCache)> ResolveAsync(
            Location location,
            bool refreshCache,
            CancellationToken cancel);
    }

    /// <summary>.</summary>
    internal readonly struct Location : IEquatable<Location>
    {
        internal readonly string AdapterId;
        internal readonly string? Category;

        internal string Kind => Category == null ? "adapter ID" : "identity";

        public static bool operator ==(Location lhs, Location rhs) => lhs.Equals(rhs);
        public static bool operator !=(Location lhs, Location rhs) => !lhs.Equals(rhs);

        public override bool Equals(object? obj) => obj is Location value && Equals(value);
        public bool Equals(Location other) => AdapterId == other.AdapterId && Category == other.Category;
        public override int GetHashCode() => HashCode.Combine(AdapterId, Category);

        public override string ToString() =>
            Category == null ? AdapterId : new Identity(AdapterId, Category).ToString();

        internal Identity ToIdentity() =>
            Category is string category ? new Identity(AdapterId, category) : throw new InvalidOperationException();

        internal Location(string location)
        {
            AdapterId = location;
            Category = null;
        }

        internal Location(Identity identity)
        {
            AdapterId = identity.Name;
            Category = identity.Category;
        }
    }

    /// <summary>The default implementation of ILocationResolver.</summary>

    internal class LocationResolver : ILocationResolver
    {
        private readonly bool _background;
        private readonly IEndpointCache? _endpointCache;
        private readonly TimeSpan _justRefreshedAge;
        private readonly IEndpointFinder _endpointFinder;

        private readonly TimeSpan _ttl;

        internal LocationResolver(
            IEndpointFinder endpointFinder,
            IEndpointCache? endpointCache,
            bool background,
            TimeSpan justRefreshedAge,
            TimeSpan ttl)
        {
            _endpointFinder = endpointFinder;
            _endpointCache = endpointCache;
            _background = background;
            _justRefreshedAge = justRefreshedAge;
            _ttl = ttl;
        }

        ValueTask<(Proxy? Proxy, bool FromCache)> ILocationResolver.ResolveAsync(
            Location location,
            bool refreshCache,
            CancellationToken cancel) => ResolveAsync(location, refreshCache, cancel);

        private async ValueTask<(Proxy? Proxy, bool FromCache)> ResolveAsync(
            Location location,
            bool refreshCache,
            CancellationToken cancel)
        {
            Proxy? proxy = null;
            bool expired = false;
            bool justRefreshed = false;
            bool resolved = false;

            if (_endpointCache != null && _endpointCache.TryGetValue(location, out (TimeSpan InsertionTime, Proxy Proxy) entry))
            {
                proxy = entry.Proxy;
                TimeSpan cacheEntryAge = Time.Elapsed - entry.InsertionTime;
                expired = _ttl != Timeout.InfiniteTimeSpan && cacheEntryAge > _ttl;
                justRefreshed = cacheEntryAge <= _justRefreshedAge;
            }

            if (proxy == null || (!_background && expired) || (refreshCache && !justRefreshed))
            {
                proxy = await _endpointFinder.FindAsync(location, cancel).ConfigureAwait(false);
                resolved = true;
            }
            else if (_background && expired)
            {
                // We retrieved an expired proxy from the cache, so we launch a refresh in the background.
                _ = _endpointFinder.FindAsync(location, cancel: default).ConfigureAwait(false);
            }

            // A well-known proxy resolution can return a loc endpoint, but not another well-known proxy loc
            // endpoint.
            if (proxy?.Endpoint?.Transport == TransportNames.Loc)
            {
                try
                {
                    // Resolves adapter ID recursively, by checking first the cache. If we resolved the well-known
                    // proxy, we request a cache refresh for the adapter.
                    (proxy, _) = await ResolveAsync(new Location(proxy!.Endpoint!.Host),
                                                    refreshCache || resolved,
                                                    cancel).ConfigureAwait(false);
                }
                finally
                {
                    // When the second resolution fails, we clear the cache entry for the initial successful
                    // resolution, since the overall resolution is a failure.
                    // proxy below can hold a loc endpoint only when an exception is thrown.
                    if (proxy == null || proxy?.Endpoint?.Transport == TransportNames.Loc)
                    {
                        _endpointCache?.Remove(location);
                    }
                }
            }

            return (proxy, proxy != null && !resolved);
        }
    }
}
