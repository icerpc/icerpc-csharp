// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Locator.Internal;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace IceRpc.Locator;

/// <summary>A locator interceptor intercepts ice requests that have no connection and have either no endpoint, and
/// attempts to assign a usable endpoint (and alt-endpoints) to such requests. This interceptor is typically installed
/// between the retry and binder interceptors.</summary>
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
        if (request.Connection == null && request.Protocol == Protocol.Ice)
        {
            Location location = default;
            bool refreshCache = false;

            EndpointSelection? endpointSelection = request.Features.Get<EndpointSelection>();
            if (endpointSelection == null)
            {
                endpointSelection = new EndpointSelection(request.Proxy);
                request.Features = request.Features.With(endpointSelection);
            }

            // We detect retries and don't use cached values for retries by setting refreshCache to true.

            if (request.Features.Get<CachedResolutionFeature>() is CachedResolutionFeature cachedResolution)
            {
                // This is the second (or greater) attempt, and we provided a cached resolution with the
                // first attempt and all subsequent attempts.

                location = cachedResolution.Location;
                refreshCache = true;
            }
            else if (endpointSelection.Endpoint == null)
            {
                if (request.Proxy.Params.TryGetValue("adapter-id", out string? adapterId))
                {
                    location = new Location { IsAdapterId = true, Value = adapterId };
                }
                else
                {
                    // Well-known proxy
                    location = new Location { Value = request.Proxy.Path };
                }
            }
            // else it could be a retry where the first attempt provided non-cached endpoint(s)

            if (location != default)
            {
                try
                {
                    (Proxy? proxy, bool fromCache) = await _locationResolver.ResolveAsync(
                        location,
                        refreshCache,
                        cancel).ConfigureAwait(false);

                    if (refreshCache)
                    {
                        if (!fromCache && !request.Features.IsReadOnly)
                        {
                            // No need to resolve this location again since we are not returning a cached value.
                            request.Features.Set<CachedResolutionFeature>(null);
                        }
                    }
                    else if (fromCache)
                    {
                        // Make sure the next attempt re-resolves location and sets refreshCache to true.
                        request.Features = request.Features.With(new CachedResolutionFeature(location));
                    }

                    if (proxy != null)
                    {
                        // A well behaved location resolver should never return a non-null proxy with a null endpoint.
                        Debug.Assert(proxy.Endpoint != null);
                        endpointSelection.Endpoint = proxy.Endpoint;
                        endpointSelection.AltEndpoints = proxy.AltEndpoints;
                    }
                    // else, resolution failed and we don't update anything
                }
                catch (Exception exception)
                {
                    // Clean-up request
                    await request.Payload.CompleteAsync(exception).ConfigureAwait(false);
                    if (request.PayloadStream != null)
                    {
                        await request.PayloadStream.CompleteAsync(exception).ConfigureAwait(false);
                    }
                    throw;
                }
            }
        }
        return await _next.InvokeAsync(request, cancel).ConfigureAwait(false);
    }

    private class CachedResolutionFeature
    {
        internal Location Location { get; }

        internal CachedResolutionFeature(Location location) => Location = location;
    }
}

/// <summary>A location is either an adapter ID or a path.</summary>
public readonly record struct Location
{
    /// <summary>Returns true when this location holds an adapter ID; otherwise, false.</summary>
    public bool IsAdapterId { get; init; }

    /// <summary>The adapter ID or path.</summary>
    public string Value { get; init; }

    internal string Kind => IsAdapterId ? "adapter ID" : "well-known proxy";

    /// <inheritdoc/>
    public override string ToString() => Value;
}

/// <summary>A location resolver resolves a location into one or more endpoints carried by a dummy proxy, and
/// optionally maintains a cache for these resolutions. It's consumed by <see cref="LocatorInterceptor"/>.
/// </summary>
public interface ILocationResolver
{
    /// <summary>Resolves a location into one or more endpoints carried by a dummy proxy.</summary>
    /// <param name="location">The location.</param>
    /// <param name="refreshCache">When <c>true</c>, requests a cache refresh.</param>
    /// <param name="cancel">The cancellation token.</param>
    /// <returns>A tuple with a nullable dummy proxy that holds the endpoint(s) (if resolved), and a bool that
    /// indicates whether these endpoints were retrieved from the implementation's cache. Proxy is null when
    /// the location resolver fails to resolve a location. When Proxy is not null, its Endpoint must be not null.
    /// </returns>
    ValueTask<(Proxy? Proxy, bool FromCache)> ResolveAsync(
        Location location,
        bool refreshCache,
        CancellationToken cancel);
}

/// <summary>Implements <see cref="ILocationResolver"/> using a locator proxy.</summary>
public class LocatorLocationResolver : ILocationResolver
{
    private readonly Lazy<ILocationResolver> _locationResolver;

    /// <summary>Constructs a locator location resolver.</summary>
    /// <param name="options">The locator options.</param>
    public LocatorLocationResolver(Configure.LocatorOptions options)
    {
        _locationResolver = new(() =>
        {
            // This is the composition root of this locator location resolver. We execute it lazily to allow the
            // application to change (configure) options after construction.

            if (options.Locator is not ILocatorPrx locator)
            {
                throw new InvalidOperationException($"{nameof(options.Locator)} is null");
            }

            if (options.Ttl != Timeout.InfiniteTimeSpan && options.RefreshThreshold >= options.Ttl)
            {
                throw new InvalidOperationException(
                    $"{nameof(options.RefreshThreshold)} must be smaller than {nameof(options.Ttl)}");
            }

            ILogger logger = options.LoggerFactory.CreateLogger("IceRpc");

            // Create and decorate endpoint cache (if caching enabled):
            IEndpointCache? endpointCache = options.Ttl != TimeSpan.Zero && options.CacheMaxSize > 0 ?
                new LogEndpointCacheDecorator(new EndpointCache(options.CacheMaxSize), logger) : null;

            // Create an decorate endpoint finder:
            IEndpointFinder endpointFinder = new LocatorEndpointFinder(locator);
            endpointFinder = new LogEndpointFinderDecorator(endpointFinder, logger);
            if (endpointCache != null)
            {
                endpointFinder = new CacheUpdateEndpointFinderDecorator(endpointFinder, endpointCache);
            }
            endpointFinder = new CoalesceEndpointFinderDecorator(endpointFinder);

            // Create and decorate location resolver:
            return new LogLocationResolverDecorator(
                endpointCache == null ? new CacheLessLocationResolver(endpointFinder) :
                    new LocationResolver(
                        endpointFinder,
                        endpointCache,
                        options.Background,
                        options.RefreshThreshold,
                        options.Ttl),
                logger);
        });
    }

    ValueTask<(Proxy? Proxy, bool FromCache)> ILocationResolver.ResolveAsync(
        Location location,
        bool refreshCache,
        CancellationToken cancel) => _locationResolver.Value.ResolveAsync(location, refreshCache, cancel);
}
