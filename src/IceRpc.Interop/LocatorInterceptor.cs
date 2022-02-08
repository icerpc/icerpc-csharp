// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace IceRpc
{
    /// <summary>A locator interceptor intercepts ice requests that have no connection and have either no endpoint or
    /// an endpoint with the "loc" transport, and attempts to assign a usable endpoint (and alt-endpoints) to such
    /// requests. This interceptor must be installed between <see cref="RetryInterceptor"/> and
    /// <see cref="BinderInterceptor"/>.</summary>
    public class LocatorInterceptor : IInvoker
    {
        private readonly IInvoker _next;
        private readonly ILocationResolver _locationResolver;

        /// <summary>Constructs a locator interceptor.</summary>
        /// <param name="next">The next invoker in the invocation pipeline.</param>
        /// <param name="locationResolver">The location resolver. It is usually created by
        /// <see cref="ILocationResolver.FromLocator"/>.</param>
        public LocatorInterceptor(IInvoker next, ILocationResolver locationResolver)
        {
            _next = next;
            _locationResolver = locationResolver;
        }

        async Task<IncomingResponse> IInvoker.InvokeAsync(OutgoingRequest request, CancellationToken cancel)
        {
            if (request.Connection == null && request.Protocol == Protocol.Ice)
            {
                Location location = default;
                bool refreshCache = false;

                // We detect retries and don't use cached values for retries by setting refreshCache to true.

                if (request.Features.Get<CachedResolutionFeature>() is CachedResolutionFeature cachedResolution)
                {
                    // This is the second (or greater) attempt, and we provided a cached resolution with the
                    // first attempt and all subsequent attempts.

                    location = cachedResolution.Location;
                    refreshCache = true;
                }
                else if (request.Endpoint == null)
                {
                    if (request.Params.TryGetValue("adapter-id", out string? adapterId))
                    {
                        location = new Location(adapterId);
                    }
                    else
                    {
                        // Well-known proxy
                        try
                        {
                            location = new Location(Identity.FromPath(request.Path));
                        }
                        catch (FormatException)
                        {
                            // ignore path that can't be converted, location remains default
                        }
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
                                // No need to resolve this location again since we are not returning a cached
                                // value.
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
                            Debug.Assert(proxy.Endpoint != null);
                            request.Endpoint = proxy.Endpoint;
                            request.AltEndpoints = proxy.AltEndpoints;
                        }
                        // else, resolution failed and we don't update anything
                    }
                    catch
                    {
                        // Ignore any exception from the location resolver. It should have been logged earlier.
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

    /// <summary>A location resolver resolves a location into a list of endpoints carried by a dummy proxy, and
    /// optionally maintains a cache for these resolutions. It's consumed by <see cref="LocatorInterceptor"/>
    /// and typically uses an <see cref="IEndpointFinder"/> and an <see cref="IEndpointCache"/> in its implementation.
    /// When the dummy proxy returned by ResolveAsync is not null, its Endpoint property is guaranteed to be not null.
    /// </summary>
    public interface ILocationResolver
    {
        /// <summary>Creates a new location resolver using a locator proxy.</summary>
        /// <param name="locator">The locator proxy.</param>
        /// <param name="options">The configuration options for this locator-based location resolver.</param>
        /// <returns>A new location resolver.</returns>
        public static ILocationResolver FromLocator(ILocatorPrx locator, Configure.LocatorOptions options)
        {
            if (options.Ttl != Timeout.InfiniteTimeSpan && options.JustRefreshedAge >= options.Ttl)
            {
                throw new ArgumentException(
                    $"{nameof(options.JustRefreshedAge)} must be smaller than {nameof(options.Ttl)}",
                    nameof(options));
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
                    new LocationResolver(endpointFinder,
                                         endpointCache,
                                         options.Background,
                                         options.JustRefreshedAge,
                                         options.Ttl),
                logger);
        }

        /// <summary>Resolves a location into a list of endpoints carried by a dummy proxy.</summary>
        /// <param name="location">The location.</param>
        /// <param name="refreshCache">When <c>true</c>, requests a cache refresh.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>A tuple with a nullable dummy proxy that holds the endpoint(s) (if resolved), and a bool that
        /// indicates whether these endpoints were retrieved from the implementation's cache. Proxy is null when
        /// the location resolver fails to resolve a location.</returns>
        ValueTask<(Proxy? Proxy, bool FromCache)> ResolveAsync(
            Location location,
            bool refreshCache,
            CancellationToken cancel);
    }

    /// <summary>A location is either an adapter ID (string) or an <see cref="Identity"/> and corresponds to the
    /// argument of <see cref="ILocator"/>'s find operations. When <see cref="Category"/> is null, the location
    /// is an adapter ID; when it's not null, the location is an Identity.</summary>
    public readonly record struct Location
    {
        /// <summary>The adapter ID/identity name.</summary>
        public readonly string AdapterId;

        /// <summary>When not null, the identity's category.</summary>
        public readonly string? Category;

        /// <summary>A string that describes the location.</summary>
        public string Kind => Category == null ? "adapter ID" : "identity";

        /// <summary>Constructs a location from an adapter ID.</summary>
        public Location(string adapterId)
        {
            AdapterId = adapterId;
            Category = null;
        }

        /// <summary>Constructs a location from an Identity.</summary>
        public Location(Identity identity)
        {
            AdapterId = identity.Name;
            Category = identity.Category;
        }

        /// <summary>Converts a location into an Identity.</summary>
        /// <returns>The identity.</returns>
        /// <exception cref="InvalidOperationException">Thrown when <see cref="Category"/> is null.</exception>
        public Identity ToIdentity() =>
            Category is string category ? new Identity(AdapterId, category) : throw new InvalidOperationException();

        /// <summary>Converts a location into a string.</summary>
        /// <returns>The adapter ID when <see cref="Category"/> is null; otherwise the identity as a string.</returns>
        public override string ToString() =>
            Category == null ? AdapterId : new Identity(AdapterId, Category).ToString();
    }
}
