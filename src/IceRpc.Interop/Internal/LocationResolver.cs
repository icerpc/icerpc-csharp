// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;

namespace IceRpc.Internal
{
    /// <summary>An implementation of <see cref="ILocationResolver"/> without a cache.</summary>
    internal class CacheLessLocationResolver : ILocationResolver
    {
        private readonly IEndpointFinder _endpointFinder;

        internal CacheLessLocationResolver(IEndpointFinder endpointFinder) => _endpointFinder = endpointFinder;

        ValueTask<(Proxy? Proxy, bool FromCache)> ILocationResolver.ResolveAsync(
            Location location,
            bool refreshCache,
            CancellationToken cancel) => ResolveAsync(location, cancel);

        private async ValueTask<(Proxy? Proxy, bool FromCache)> ResolveAsync(
            Location location,
            CancellationToken cancel)
        {
            Proxy? proxy = await _endpointFinder.FindAsync(location, cancel).ConfigureAwait(false);

            // A well-known proxy resolution can return a loc endpoint:
            if (proxy != null && proxy.Endpoint!.Transport == TransportNames.Loc)
            {
                (proxy, _) = await ResolveAsync(new Location(proxy!.Endpoint!.Host), cancel).ConfigureAwait(false);
            }

            return (proxy, false);
        }
    }

    /// <summary>The main implementation of <see cref="ILocationResolver"/>, with a cache.</summary>
    internal class LocationResolver : ILocationResolver
    {
        private readonly bool _background;
        private readonly IEndpointCache _endpointCache;
        private readonly TimeSpan _justRefreshedAge;
        private readonly IEndpointFinder _endpointFinder;

        private readonly TimeSpan _ttl;

        internal LocationResolver(
            IEndpointFinder endpointFinder,
            IEndpointCache endpointCache,
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

            if (_endpointCache.TryGetValue(location, out (TimeSpan InsertionTime, Proxy Proxy) entry))
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

            // A well-known proxy resolution can return a loc endpoint
            if (proxy != null && proxy.Endpoint!.Transport == TransportNames.Loc)
            {
                try
                {
                    // Resolves adapter ID recursively, by checking first the cache. If we resolved the well-known
                    // proxy, we request a cache refresh for the adapter ID.
                    (proxy, _) = await ResolveAsync(new Location(proxy!.Endpoint!.Host),
                                                    refreshCache || resolved,
                                                    cancel).ConfigureAwait(false);
                }
                finally
                {
                    // When the second resolution fails, we clear the cache entry for the initial successful
                    // resolution, since the overall resolution is a failure.
                    // proxy below can hold a loc endpoint only when an exception is thrown.
                    if (proxy == null || proxy.Endpoint!.Transport == TransportNames.Loc)
                    {
                        _endpointCache.Remove(location);
                    }
                }
            }

            return (proxy, proxy != null && !resolved);
        }
    }

    /// <summary>A decorator that adds logging to a location resolver.</summary>
    internal class LogLocationResolverDecorator : ILocationResolver
    {
        private readonly ILocationResolver _decoratee;
        private readonly ILogger _logger;

        internal LogLocationResolverDecorator(ILocationResolver decoratee, ILogger logger)
        {
            _decoratee = decoratee;
            _logger = logger;
        }

        async ValueTask<(Proxy? Proxy, bool FromCache)> ILocationResolver.ResolveAsync(
            Location location,
            bool refreshCache,
            CancellationToken cancel)
        {
            _logger.LogResolving(location.Kind, location);

            try
            {
                (Proxy? proxy, bool fromCache) =
                    await _decoratee.ResolveAsync(location, refreshCache, cancel).ConfigureAwait(false);

                if (proxy == null)
                {
                    _logger.LogFailedToResolve(location.Kind, location);
                }
                else
                {
                    _logger.LogResolved(location.Kind, location, proxy);
                }

                return (proxy, fromCache);
            }
            catch (Exception ex)
            {
                _logger.LogFailedToResolve(location.Kind, location, ex);
                throw;
            }
        }
    }

    /// <summary>This class contains ILogger extension methods used by LogLocationResolverDecorator.</summary>
    internal static partial class LocatorLoggerExtensions
    {
        [LoggerMessage(
            EventId = (int)LocationEvent.Resolving,
            EventName = nameof(LocationEvent.Resolving),
            Level = LogLevel.Trace,
            Message = "resolving {LocationKind} {Location}")]
        internal static partial void LogResolving(this ILogger logger, string locationKind, Location location);

        [LoggerMessage(
            EventId = (int)LocationEvent.Resolved,
            EventName = nameof(LocationEvent.Resolved),
            Level = LogLevel.Debug,
            Message = "resolved {LocationKind} '{Location}' = '{Proxy}'")]
        internal static partial void LogResolved(
            this ILogger logger,
            string locationKind,
            Location location,
            Proxy proxy);

        [LoggerMessage(
            EventId = (int)LocationEvent.FailedToResolve,
            EventName = nameof(LocationEvent.FailedToResolve),
            Level = LogLevel.Debug,
            Message = "failed to resolve {LocationKind} '{Location}'")]
        internal static partial void LogFailedToResolve(
            this ILogger logger,
            string locationKind,
            Location location,
            Exception? exception = null);
    }
}
