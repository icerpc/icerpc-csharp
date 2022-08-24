// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Locator.Internal;

/// <summary>An implementation of <see cref="ILocationResolver"/> without a cache.</summary>
internal class CacheLessLocationResolver : ILocationResolver
{
    private readonly IServerAddressFinder _serverAddressFinder;

    internal CacheLessLocationResolver(IServerAddressFinder serverAddressFinder) =>
        _serverAddressFinder = serverAddressFinder;

    public ValueTask<(ServiceAddress? ServiceAddress, bool FromCache)> ResolveAsync(
        Location location,
        bool refreshCache,
        CancellationToken cancellationToken) => ResolveAsync(location, cancellationToken);

    private async ValueTask<(ServiceAddress? ServiceAddress, bool FromCache)> ResolveAsync(
        Location location,
        CancellationToken cancellationToken)
    {
        ServiceAddress? serviceAddress = await _serverAddressFinder.FindAsync(location, cancellationToken).ConfigureAwait(false);

        // A well-known service address resolution can return a service address with an adapter ID
        if (serviceAddress is not null && serviceAddress.Params.TryGetValue("adapter-id", out string? adapterId))
        {
            (serviceAddress, _) = await ResolveAsync(
                new Location { IsAdapterId = true, Value = adapterId },
                cancellationToken).ConfigureAwait(false);
        }

        return (serviceAddress, false);
    }
}

/// <summary>The main implementation of <see cref="ILocationResolver"/>, with a cache.</summary>
internal class LocationResolver : ILocationResolver
{
    private readonly bool _background;
    private readonly IServerAddressCache _serverAddressCache;
    private readonly IServerAddressFinder _serverAddressFinder;
    private readonly TimeSpan _refreshThreshold;

    private readonly TimeSpan _ttl;

    internal LocationResolver(
        IServerAddressFinder serverAddressFinder,
        IServerAddressCache serverAddressCache,
        bool background,
        TimeSpan refreshThreshold,
        TimeSpan ttl)
    {
        _serverAddressFinder = serverAddressFinder;
        _serverAddressCache = serverAddressCache;
        _background = background;
        _refreshThreshold = refreshThreshold;
        _ttl = ttl;
    }

    public ValueTask<(ServiceAddress? ServiceAddress, bool FromCache)> ResolveAsync(
        Location location,
        bool refreshCache,
        CancellationToken cancellationToken) => PerformResolveAsync(location, refreshCache, cancellationToken);

    private async ValueTask<(ServiceAddress? ServiceAddress, bool FromCache)> PerformResolveAsync(
        Location location,
        bool refreshCache,
        CancellationToken cancellationToken)
    {
        ServiceAddress? serviceAddress = null;
        bool expired = false;
        bool justRefreshed = false;
        bool resolved = false;

        if (_serverAddressCache.TryGetValue(location, out (TimeSpan InsertionTime, ServiceAddress ServiceAddress) entry))
        {
            serviceAddress = entry.ServiceAddress;
            TimeSpan cacheEntryAge = TimeSpan.FromMilliseconds(Environment.TickCount64) - entry.InsertionTime;
            expired = _ttl != Timeout.InfiniteTimeSpan && cacheEntryAge > _ttl;
            justRefreshed = cacheEntryAge <= _refreshThreshold;
        }

        if (serviceAddress is null || (!_background && expired) || (refreshCache && !justRefreshed))
        {
            serviceAddress = await _serverAddressFinder.FindAsync(location, cancellationToken).ConfigureAwait(false);
            resolved = true;
        }
        else if (_background && expired)
        {
            // We retrieved an expired service address from the cache, so we launch a refresh in the background.
            _ = _serverAddressFinder.FindAsync(location, cancellationToken: default).ConfigureAwait(false);
        }

        // A well-known service address resolution can return a service address with an adapter-id.
        if (serviceAddress is not null && serviceAddress.Params.TryGetValue("adapter-id", out string? adapterId))
        {
            try
            {
                // Resolves adapter ID recursively, by checking first the cache. If we resolved the well-known
                // service address, we request a cache refresh for the adapter ID.
                (serviceAddress, _) = await PerformResolveAsync(
                    new Location { IsAdapterId = true, Value = adapterId },
                    refreshCache || resolved,
                    cancellationToken).ConfigureAwait(false);
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
                    _serverAddressCache.Remove(location);
                }
            }
        }

        return (serviceAddress, serviceAddress is not null && !resolved);
    }
}

/// <summary>A decorator that adds event source logging to a location resolver.</summary>
internal class LogLocationResolverDecorator : ILocationResolver
{
    private readonly ILocationResolver _decoratee;

    public async ValueTask<(ServiceAddress? ServiceAddress, bool FromCache)> ResolveAsync(
        Location location,
        bool refreshCache,
        CancellationToken cancellationToken)
    {
        LocatorEventSource.Log.ResolveStart(location);
        ServiceAddress? serviceAddress = null;

        try
        {
            (serviceAddress, bool fromCache) =
                await _decoratee.ResolveAsync(location, refreshCache, cancellationToken).ConfigureAwait(false);

            return (serviceAddress, fromCache);
        }
        catch (Exception exception)
        {
            LocatorEventSource.Log.ResolveFailure(location, exception);
            throw;
        }
        finally
        {
            LocatorEventSource.Log.ResolveStop(location, serviceAddress);
        }
    }

    internal LogLocationResolverDecorator(ILocationResolver decoratee) => _decoratee = decoratee;
}
