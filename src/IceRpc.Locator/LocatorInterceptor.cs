// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Locator.Internal;
using IceRpc.Slice.Ice;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Immutable;
using System.Diagnostics;

namespace IceRpc.Locator;

/// <summary>A locator interceptor intercepts ice requests that have no server address and attempts to assign a usable
/// server address (and alt-server addresses) to such requests via the <see cref="IServerAddressFeature" />. You would
/// usually install the retry interceptor before the locator interceptor in the invocation pipeline and use the
/// connection cache invoker for the pipeline, with this setup the locator interceptor would be able to detect
/// invocation retries and refreshes the server address when required, and the connection cache would take care of
/// creating the connections for the resolved server address.</summary>
public class LocatorInterceptor : IInvoker
{
    private readonly IInvoker _next;
    private readonly ILocationResolver _locationResolver;

    /// <summary>Constructs a locator interceptor.</summary>
    /// <param name="next">The next invoker in the invocation pipeline.</param>
    /// <param name="locationResolver">The location resolver. It is usually a <see cref="LocatorLocationResolver" />.
    /// </param>
    public LocatorInterceptor(IInvoker next, ILocationResolver locationResolver)
    {
        _next = next;
        _locationResolver = locationResolver;
    }

    /// <inheritdoc/>
    public async Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancellationToken)
    {
        if (request.Protocol == Protocol.Ice && request.ServiceAddress.ServerAddress is null)
        {
            Location location = default;
            bool refreshCache = false;

            if (request.Features.Get<IServerAddressFeature>() is not IServerAddressFeature serverAddressFeature)
            {
                serverAddressFeature = new ServerAddressFeature(request.ServiceAddress);
                request.Features = request.Features.With(serverAddressFeature);
            }

            // We detect retries and don't use cached values for retries by setting refreshCache to true.

            if (request.Features.Get<ICachedResolutionFeature>() is ICachedResolutionFeature cachedResolution)
            {
                // This is the second (or greater) attempt, and we provided a cached resolution with the
                // first attempt and all subsequent attempts.

                location = cachedResolution.Location;
                refreshCache = true;
            }
            else if (serverAddressFeature.ServerAddress is null)
            {
                location = request.ServiceAddress.Params.TryGetValue("adapter-id", out string? adapterId) ?
                    new Location { IsAdapterId = true, Value = adapterId } :
                    new Location { Value = request.ServiceAddress.Path };
            }
            // else it could be a retry where the first attempt provided non-cached server address(es)

            if (location != default)
            {
                (ServiceAddress? serviceAddress, bool fromCache) = await _locationResolver.ResolveAsync(
                    location,
                    refreshCache,
                    cancellationToken).ConfigureAwait(false);

                if (refreshCache)
                {
                    if (!fromCache && !request.Features.IsReadOnly)
                    {
                        // No need to resolve this location again since we are not returning a cached value.
                        request.Features.Set<ICachedResolutionFeature>(null);
                    }
                }
                else if (fromCache)
                {
                    // Make sure the next attempt re-resolves location and sets refreshCache to true.
                    request.Features = request.Features.With<ICachedResolutionFeature>(
                        new CachedResolutionFeature(location));
                }

                if (serviceAddress is not null)
                {
                    // A well behaved location resolver should never return a non-null service address with a null
                    // serverAddress.
                    Debug.Assert(serviceAddress.ServerAddress is not null);

                    // Before assigning the new resolved server addresses to the server address feature we have to
                    // remove any server addresses that are included in the list of removed server addresses, to
                    // avoid retrying with a server address that has been already excluded for the invocation.
                    (ServerAddress? serverAddress, ImmutableList<ServerAddress> altServerAddresses) =
                        ComputeServerAddresses(serviceAddress, serverAddressFeature.RemovedServerAddresses);
                    serverAddressFeature.ServerAddress = serverAddress;
                    serverAddressFeature.AltServerAddresses = altServerAddresses;
                }
                // else, resolution failed and we don't update anything
            }
        }
        return await _next.InvokeAsync(request, cancellationToken).ConfigureAwait(false);

        static (ServerAddress? ServerAddress, ImmutableList<ServerAddress> AltServerAddresses) ComputeServerAddresses(
            ServiceAddress serviceAddress,
            IEnumerable<ServerAddress> excludedAddresses)
        {
            (ServerAddress? ServerAddress, ImmutableList<ServerAddress> AltServerAddresses) result =
                (serviceAddress.ServerAddress, serviceAddress.AltServerAddresses);
            if (result.ServerAddress is ServerAddress serverAddress && excludedAddresses.Contains(serverAddress))
            {
                result.ServerAddress = null;
            }
            result.AltServerAddresses = result.AltServerAddresses.RemoveAll(e => excludedAddresses.Contains(e));

            if (result.ServerAddress is null && result.AltServerAddresses.Count > 0)
            {
                result.ServerAddress = result.AltServerAddresses[0];
                result.AltServerAddresses = result.AltServerAddresses.RemoveAt(0);
            }
            return result;
        }
    }

    private interface ICachedResolutionFeature
    {
        Location Location { get; }
    }

    private class CachedResolutionFeature : ICachedResolutionFeature
    {
        public Location Location { get; }

        internal CachedResolutionFeature(Location location) => Location = location;
    }
}

/// <summary>A location is either an adapter ID or a path.</summary>
public readonly record struct Location
{
    /// <summary>Gets a value indicating whether or not this location holds an adapter ID; otherwise,
    /// <see langword="false" />.</summary>
    public bool IsAdapterId { get; init; }

    /// <summary>Gets the adapter ID or path.</summary>
    public string Value { get; init; }

    internal string Kind => IsAdapterId ? "adapter ID" : "well-known service address";

    /// <summary>Returns <see cref="Value"/>.</summary>
    /// <returns>The adapter ID or path.</returns>
    public override string ToString() => Value;
}

/// <summary>A location resolver resolves a location into one or more server addresses carried by a dummy service
/// address, and optionally maintains a cache for these resolutions. It's the "brain" of
/// <see cref="LocatorInterceptor" />. The same location resolver can be shared by multiple locator interceptors.
/// </summary>
public interface ILocationResolver
{
    /// <summary>Resolves a location into one or more server addresses carried by a dummy service address.</summary>
    /// <param name="location">The location to resolve.</param>
    /// <param name="refreshCache">When <see langword="true" />, requests a cache refresh.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A tuple with a nullable dummy service address that holds the server addresses (if resolved), and a bool
    /// that indicates whether these server addresses were retrieved from the implementation's cache. ServiceAddress is
    /// <see langword="null" /> when the location resolver fails to resolve a location. When ServiceAddress is not null,
    /// its ServerAddress is not <see langword="null" />.</returns>
    ValueTask<(ServiceAddress? ServiceAddress, bool FromCache)> ResolveAsync(
        Location location,
        bool refreshCache,
        CancellationToken cancellationToken);
}

/// <summary>Implements <see cref="ILocationResolver" /> using an <see cref="ILocator"/>.</summary>
public class LocatorLocationResolver : ILocationResolver
{
    private readonly ILocationResolver _locationResolver;

    /// <summary>Constructs a locator location resolver.</summary>
    /// <param name="locator">The locator.</param>
    /// <param name="options">The locator options.</param>
    /// <param name="logger">The logger.</param>
    public LocatorLocationResolver(ILocator locator, LocatorOptions options, ILogger logger)
    {
        // This is the composition root of this locator location resolver.
        if (options.Ttl != Timeout.InfiniteTimeSpan && options.RefreshThreshold >= options.Ttl)
        {
            throw new InvalidOperationException(
                $"The value of {nameof(options.RefreshThreshold)} must be smaller than the value of {nameof(options.Ttl)}.");
        }

        // Create and decorate server address cache (if caching enabled):
        IServerAddressCache? serverAddressCache = options.Ttl != TimeSpan.Zero && options.MaxCacheSize > 0 ?
            new ServerAddressCache(options.MaxCacheSize) : null;
        if (serverAddressCache is not null && logger != NullLogger.Instance)
        {
            serverAddressCache = new LogServerAddressCacheDecorator(serverAddressCache, logger);
        }

        // Create and decorate server address finder:
        IServerAddressFinder serverAddressFinder = new LocatorServerAddressFinder(locator);
        if (logger != NullLogger.Instance)
        {
            serverAddressFinder = new LogServerAddressFinderDecorator(serverAddressFinder, logger);
        }

        if (serverAddressCache is not null)
        {
            serverAddressFinder = new CacheUpdateServerAddressFinderDecorator(serverAddressFinder, serverAddressCache);
        }
        serverAddressFinder = new CoalesceServerAddressFinderDecorator(serverAddressFinder);

        _locationResolver = serverAddressCache is null ?
            new CacheLessLocationResolver(serverAddressFinder) :
            new LocationResolver(
                serverAddressFinder,
                serverAddressCache,
                options.Background,
                options.RefreshThreshold,
                options.Ttl);
        if (logger != NullLogger.Instance)
        {
            _locationResolver = new LogLocationResolverDecorator(_locationResolver, logger);
        }
    }

    /// <inheritdoc/>
    public ValueTask<(ServiceAddress? ServiceAddress, bool FromCache)> ResolveAsync(
        Location location,
        bool refreshCache,
        CancellationToken cancellationToken) =>
        _locationResolver.ResolveAsync(location, refreshCache, cancellationToken);
}
