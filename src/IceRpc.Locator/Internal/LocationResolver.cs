// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Locator.Internal;

/// <summary>This class contains ILogger extension methods used by LogLocationResolverDecorator.</summary>
internal static partial class LocatorLoggerExtensions
{
    [LoggerMessage(
        EventId = (int)LocationEventIds.Resolving,
        EventName = nameof(LocationEventIds.Resolving),
        Level = LogLevel.Trace,
        Message = "resolving {LocationKind} {Location}")]
    internal static partial void LogResolving(this ILogger logger, string locationKind, Location location);

    [LoggerMessage(
        EventId = (int)LocationEventIds.Resolved,
        EventName = nameof(LocationEventIds.Resolved),
        Level = LogLevel.Debug,
        Message = "resolved {LocationKind} '{Location}' = '{ServiceAddress}'")]
    internal static partial void LogResolved(
        this ILogger logger,
        string locationKind,
        Location location,
        ServiceAddress serviceAddress);

    [LoggerMessage(
        EventId = (int)LocationEventIds.FailedToResolve,
        EventName = nameof(LocationEventIds.FailedToResolve),
        Level = LogLevel.Debug,
        Message = "failed to resolve {LocationKind} '{Location}'")]
    internal static partial void LogFailedToResolve(
        this ILogger logger,
        string locationKind,
        Location location,
        Exception? exception = null);
}

/// <summary>An implementation of <see cref="ILocationResolver"/> without a cache.</summary>
internal class CacheLessLocationResolver : ILocationResolver
{
    private readonly IEndpointFinder _endpointFinder;

    internal CacheLessLocationResolver(IEndpointFinder endpointFinder) => _endpointFinder = endpointFinder;

    ValueTask<(ServiceAddress? ServiceAddress, bool FromCache)> ILocationResolver.ResolveAsync(
        Location location,
        bool refreshCache,
        CancellationToken cancel) => ResolveAsync(location, cancel);

    private async ValueTask<(ServiceAddress? ServiceAddress, bool FromCache)> ResolveAsync(
        Location location,
        CancellationToken cancel)
    {
        ServiceAddress? serviceAddress = await _endpointFinder.FindAsync(location, cancel).ConfigureAwait(false);

        // A well-known service address resolution can return a service address with an adapter ID
        if (serviceAddress is not null && serviceAddress.Params.TryGetValue("adapter-id", out string? adapterId))
        {
            (serviceAddress, _) = await ResolveAsync(
                new Location { IsAdapterId = true, Value = adapterId },
                cancel).ConfigureAwait(false);
        }

        return (serviceAddress, false);
    }
}

/// <summary>The main implementation of <see cref="ILocationResolver"/>, with a cache.</summary>
internal class LocationResolver : ILocationResolver
{
    private readonly bool _background;
    private readonly IEndpointCache _endpointCache;
    private readonly IEndpointFinder _endpointFinder;
    private readonly TimeSpan _refreshThreshold;

    private readonly TimeSpan _ttl;

    internal LocationResolver(
        IEndpointFinder endpointFinder,
        IEndpointCache endpointCache,
        bool background,
        TimeSpan refreshThreshold,
        TimeSpan ttl)
    {
        _endpointFinder = endpointFinder;
        _endpointCache = endpointCache;
        _background = background;
        _refreshThreshold = refreshThreshold;
        _ttl = ttl;
    }

    public ValueTask<(ServiceAddress? ServiceAddress, bool FromCache)> ResolveAsync(
        Location location,
        bool refreshCache,
        CancellationToken cancel) => PerformResolveAsync(location, refreshCache, cancel);

    private async ValueTask<(ServiceAddress? ServiceAddress, bool FromCache)> PerformResolveAsync(
        Location location,
        bool refreshCache,
        CancellationToken cancel)
    {
        ServiceAddress? serviceAddress = null;
        bool expired = false;
        bool justRefreshed = false;
        bool resolved = false;

        if (_endpointCache.TryGetValue(location, out (TimeSpan InsertionTime, ServiceAddress ServiceAddress) entry))
        {
            serviceAddress = entry.ServiceAddress;
            TimeSpan cacheEntryAge = TimeSpan.FromMilliseconds(Environment.TickCount64) - entry.InsertionTime;
            expired = _ttl != Timeout.InfiniteTimeSpan && cacheEntryAge > _ttl;
            justRefreshed = cacheEntryAge <= _refreshThreshold;
        }

        if (serviceAddress is null || (!_background && expired) || (refreshCache && !justRefreshed))
        {
            serviceAddress = await _endpointFinder.FindAsync(location, cancel).ConfigureAwait(false);
            resolved = true;
        }
        else if (_background && expired)
        {
            // We retrieved an expired serviceAddress from the cache, so we launch a refresh in the background.
            _ = _endpointFinder.FindAsync(location, cancel: default).ConfigureAwait(false);
        }

        // A well-known serviceAddress resolution can return a serviceAddress with an adapter-id
        if (serviceAddress is not null && serviceAddress.Params.TryGetValue("adapter-id", out string? adapterId))
        {
            try
            {
                // Resolves adapter ID recursively, by checking first the cache. If we resolved the well-known
                // serviceAddress, we request a cache refresh for the adapter ID.
                (serviceAddress, _) = await PerformResolveAsync(
                    new Location { IsAdapterId = true, Value = adapterId },
                    refreshCache || resolved,
                    cancel).ConfigureAwait(false);
            }
            catch
            {
                serviceAddress = null;
                throw;
            }
            finally
            {
                // When the second resolution fails, we clear the cache entry for the initial successful
                // resolution, since the overall resolution is a failure.
                if (serviceAddress is null)
                {
                    _endpointCache.Remove(location);
                }
            }
        }

        return (serviceAddress, serviceAddress is not null && !resolved);
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

    async ValueTask<(ServiceAddress? ServiceAddress, bool FromCache)> ILocationResolver.ResolveAsync(
        Location location,
        bool refreshCache,
        CancellationToken cancel)
    {
        _logger.LogResolving(location.Kind, location);

        try
        {
            (ServiceAddress? serviceAddress, bool fromCache) =
                await _decoratee.ResolveAsync(location, refreshCache, cancel).ConfigureAwait(false);

            if (serviceAddress is null)
            {
                _logger.LogFailedToResolve(location.Kind, location);
            }
            else
            {
                _logger.LogResolved(location.Kind, location, serviceAddress);
            }

            return (serviceAddress, fromCache);
        }
        catch (Exception ex)
        {
            _logger.LogFailedToResolve(location.Kind, location, ex);
            throw;
        }
    }
}
