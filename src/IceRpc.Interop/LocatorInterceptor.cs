// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Interop;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    /// <summary>An options class for configuring a Locator interceptor.</summary>
    public sealed class LocatorOptions
    {
        /// <summary>When true, if the lookup finds a stale cache entry, it returns the stale entry's endpoint(s)
        /// and executes a call "in the background" to refresh this entry. The default is false, meaning the lookup
        /// does not return stale values.</summary>
        public bool Background { get; set; }

        /// <summary>The maximum size of the cache. Must be 0 (meaning no cache) or greater. The default value is
        /// 100.</summary>
        public int CacheMaxSize
        {
            get => _cacheMaxSize;
            set => _cacheMaxSize = value >= 0 ? value :
                throw new ArgumentException($"{nameof(CacheMaxSize)} must be positive", nameof(value));
        }

        /// <summary>When a cache entry's age is <c>JustRefreshedAge</c> or less, it's considered just refreshed and
        /// won't be updated even when the caller requests a refresh.</summary>
        public TimeSpan JustRefreshedAge { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>The logger factory used to create the IceRpc logger.</summary>
        public ILoggerFactory LoggerFactory { get; set; } = NullLoggerFactory.Instance;

        /// <summary>After ttl, a cache entry is considered stale. The default value is InfiniteTimeSpan, meaning
        /// the cache entries never become stale.</summary>
        public TimeSpan Ttl { get; set; } = Timeout.InfiniteTimeSpan;

        private int _cacheMaxSize = 100;
    }

    /// <summary>A locator interceptor is responsible for resolving "loc" endpoints, the locator interceptor is no-op
    /// when the request carries a connection; otherwise it "resolves" the endpoints of the request using an
    /// <see cref="ILocatorPrx"/> such as IceGrid. It must be installed between <see cref="RetryInterceptor"/> and
    /// <see cref="BinderInterceptor"/>.</summary>
    public class LocatorInterceptor : IInvoker
    {
        private readonly IInvoker _next;
        private readonly ILocationResolver _locationResolver;

        /// <summary>Constructs a locator interceptor.</summary>
        /// <param name="next">The next invoker in the invocation pipeline.</param>
        /// <param name="locator">The locator proxy used for the resolutions.</param>
        /// <param name="options">The options of this interceptor.</param>
        /// <returns>A new locator interceptor.</returns>
        public LocatorInterceptor(IInvoker next, ILocatorPrx locator, LocatorOptions options)
        {
            _next = next;

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
            _locationResolver = new LogLocationResolverDecorator(
                endpointCache == null ? new CacheLessLocationResolver(endpointFinder) :
                    new LocationResolver(endpointFinder,
                                         endpointCache,
                                         options.Background,
                                         options.JustRefreshedAge,
                                         options.Ttl),
                logger);
        }

        async Task<IncomingResponse> IInvoker.InvokeAsync(OutgoingRequest request, CancellationToken cancel)
        {
            if (request.Connection == null && request.Protocol == Protocol.Ice1)
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
                else if (request.Endpoint is Endpoint endpoint && endpoint.Transport == TransportNames.Loc)
                {
                    // Typically first attempt since a successful resolution replaces this loc endpoint.
                    location = new Location(endpoint.Host);
                }
                else if (request.Endpoint == null)
                {
                    // Well-known proxy
                    try
                    {
                        var identityAndFacet = IdentityAndFacet.FromPath(request.Path);
                        location = new Location(identityAndFacet.Identity);
                    }
                    catch (FormatException)
                    {
                        // ignore path that can't be converted, location remains default
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

                            if (request.Features.IsReadOnly)
                            {
                                request.Features = new FeatureCollection(request.Features);
                            }
                            request.Features.Set(new CachedResolutionFeature(location));
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
}
