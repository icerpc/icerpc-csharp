// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Locator.Internal;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace IceRpc.Locator;

/// <summary>A locator interceptor intercepts ice requests that have no connection and no endpoint, and attempts to
/// assign a usable endpoint (and alt-endpoints) to such requests.</summary>
public class LocatorInterceptor : IInvoker
{
    private readonly IInvoker _next;
    private readonly ILocationResolver _locationResolver;

    /// <summary>Constructs a locator interceptor.</summary>
    /// <param name="next">The next invoker in the invocation pipeline.</param>
    /// <param name="locationResolver">The location resolver. It is usually a <see cref="LocatorLocationResolver"/>.
    /// </param>
    public LocatorInterceptor(IInvoker next, ILocationResolver locationResolver)
    {
        _next = next;
        _locationResolver = locationResolver;
    }

    /// <inheritdoc/>
    public async Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel)
    {
        IEndpointFeature? endpointFeature = request.Features.Get<IEndpointFeature>();

        if (endpointFeature?.Connection is null && request.Protocol == Protocol.Ice)
        {
            Location location = default;
            bool refreshCache = false;

            if (endpointFeature is null)
            {
                endpointFeature = new EndpointFeature(request.ServiceAddress);
                request.Features = request.Features.With(endpointFeature);
            }

            // We detect retries and don't use cached values for retries by setting refreshCache to true.

            if (request.Features.Get<ICachedResolutionFeature>() is ICachedResolutionFeature cachedResolution)
            {
                // This is the second (or greater) attempt, and we provided a cached resolution with the
                // first attempt and all subsequent attempts.

                location = cachedResolution.Location;
                refreshCache = true;
            }
            else if (endpointFeature.Endpoint is null)
            {
                location = request.ServiceAddress.Params.TryGetValue("adapter-id", out string? adapterId) ?
                    new Location { IsAdapterId = true, Value = adapterId } :
                    new Location { Value = request.ServiceAddress.Path };
            }
            // else it could be a retry where the first attempt provided non-cached endpoint(s)

            if (location != default)
            {
                (ServiceAddress? proxy, bool fromCache) = await _locationResolver.ResolveAsync(
                    location,
                    refreshCache,
                    cancel).ConfigureAwait(false);

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

                if (proxy is not null)
                {
                    // A well behaved location resolver should never return a non-null proxy with a null endpoint.
                    Debug.Assert(proxy.Endpoint is not null);
                    endpointFeature.Endpoint = proxy.Endpoint;
                    endpointFeature.AltEndpoints = proxy.AltEndpoints;
                }
                // else, resolution failed and we don't update anything
            }
        }
        return await _next.InvokeAsync(request, cancel).ConfigureAwait(false);
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
    /// <summary>Gets a value indicating whether or not this location holds an adapter ID; otherwise, false.</summary>
    public bool IsAdapterId { get; init; }

    /// <summary>Gets the adapter ID or path.</summary>
    public string Value { get; init; }

    internal string Kind => IsAdapterId ? "adapter ID" : "well-known proxy";

    /// <inheritdoc/>
    public override string ToString() => Value;
}

/// <summary>A location resolver resolves a location into one or more endpoints carried by a dummy service address, and
/// optionally maintains a cache for these resolutions. It's consumed by <see cref="LocatorInterceptor"/>.
/// </summary>
public interface ILocationResolver
{
    /// <summary>Resolves a location into one or more endpoints carried by a dummy service address.</summary>
    /// <param name="location">The location.</param>
    /// <param name="refreshCache">When <c>true</c>, requests a cache refresh.</param>
    /// <param name="cancel">The cancellation token.</param>
    /// <returns>A tuple with a nullable dummy service address that holds the endpoint(s) (if resolved), and a bool that
    /// indicates whether these endpoints were retrieved from the implementation's cache. ServiceAddress is null when
    /// the location resolver fails to resolve a location. When ServiceAddress is not null, its Endpoint must be not
    /// null.</returns>
    ValueTask<(ServiceAddress? ServiceAddress, bool FromCache)> ResolveAsync(
        Location location,
        bool refreshCache,
        CancellationToken cancel);
}

/// <summary>Implements <see cref="ILocationResolver"/> using a locator proxy.</summary>
public class LocatorLocationResolver : ILocationResolver
{
    private readonly ILocationResolver _locationResolver;

    /// <summary>Constructs a locator location resolver.</summary>
    /// <param name="locator">The locator proxy.</param>
    /// <param name="loggerFactory">The logger factory.</param>
    /// <param name="options">The locator options.</param>
    public LocatorLocationResolver(ILocatorPrx locator, ILoggerFactory loggerFactory, LocatorOptions options)
    {
        // This is the composition root of this locator location resolver.

        if (options.Ttl != Timeout.InfiniteTimeSpan && options.RefreshThreshold >= options.Ttl)
        {
            throw new InvalidOperationException(
                $"{nameof(options.RefreshThreshold)} must be smaller than {nameof(options.Ttl)}");
        }

        ILogger logger = loggerFactory.CreateLogger("IceRpc.Locator");
        bool installLogDecorator = logger.IsEnabled(LogLevel.Information);

        // Create and decorate endpoint cache (if caching enabled):
        IEndpointCache? endpointCache = options.Ttl != TimeSpan.Zero && options.MaxCacheSize > 0 ?
            new EndpointCache(options.MaxCacheSize) : null;

        if (endpointCache is not null && installLogDecorator)
        {
            endpointCache = new LogEndpointCacheDecorator(endpointCache, logger);
        }

        // Create and decorate endpoint finder:
        IEndpointFinder endpointFinder = new LocatorEndpointFinder(locator);
        if (installLogDecorator)
        {
            endpointFinder = new LogEndpointFinderDecorator(endpointFinder, logger);
        }
        if (endpointCache is not null)
        {
            endpointFinder = new CacheUpdateEndpointFinderDecorator(endpointFinder, endpointCache);
        }
        endpointFinder = new CoalesceEndpointFinderDecorator(endpointFinder);

        _locationResolver = endpointCache is null ? new CacheLessLocationResolver(endpointFinder) :
                new LocationResolver(
                    endpointFinder,
                    endpointCache,
                    options.Background,
                    options.RefreshThreshold,
                    options.Ttl);

        if (installLogDecorator)
        {
            _locationResolver = new LogLocationResolverDecorator(_locationResolver, logger);
        }
    }

    ValueTask<(ServiceAddress? ServiceAddress, bool FromCache)> ILocationResolver.ResolveAsync(
        Location location,
        bool refreshCache,
        CancellationToken cancel) => _locationResolver.ResolveAsync(location, refreshCache, cancel);
}
