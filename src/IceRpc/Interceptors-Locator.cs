// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Interop;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Diagnostics;
using System.Threading;

namespace IceRpc
{
    public static partial class Interceptors
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

        /// <summary>Creates a locator interceptor with default options.</summary>
        /// <param name="locator">The locator proxy used for the resolutions.</param>
        /// <returns>A new locator interceptor.</returns>
        public static Func<IInvoker, IInvoker> Locator(ILocatorPrx locator) => Locator(locator, new());

        /// <summary>Creates a locator interceptor. A locator interceptor is no-op when the request carries a
        /// connection; otherwise it "resolves" the endpoints of the request using an <see cref="ILocatorPrx"/> such as
        /// IceGrid. It must be installed between <see cref="Retry"/> and <see cref="Binder"/>.</summary>
        /// <param name="locator">The locator proxy used for the resolutions.</param>
        /// <param name="options">The options of this interceptor.</param>
        /// <returns>A new locator interceptor.</returns>
        public static Func<IInvoker, IInvoker> Locator(ILocatorPrx locator, LocatorOptions options)
        {
            if (options.Ttl != Timeout.InfiniteTimeSpan && options.JustRefreshedAge >= options.Ttl)
            {
                throw new ArgumentException(
                    $"{nameof(options.JustRefreshedAge)} must be smaller than {nameof(options.Ttl)}", nameof(options));
            }

            ILogger logger = options.LoggerFactory.CreateLogger("IceRpc");

            IEndpointCache? endpointCache = options.Ttl != TimeSpan.Zero && options.CacheMaxSize > 0 ?
                new LogEndpointCacheDecorator(new EndpointCache(options.CacheMaxSize), logger) : null;

            IEndpointFinder endpointFinder = new LocatorEndpointFinder(locator);

            // Install decorators
            endpointFinder = new LogEndpointFinderDecorator(endpointFinder, logger);
            if (endpointCache != null)
            {
                endpointFinder = new CacheUpdateEndpointFinderDecorator(endpointFinder, endpointCache);
            }
            endpointFinder = new CoalesceEndpointFinderDecorator(endpointFinder);

            ILocationResolver locationResolver = new LogLocationResolverDecorator(
                new LocationResolver(endpointFinder,
                                     endpointCache,
                                     options.Background,
                                     options.JustRefreshedAge,
                                     options.Ttl),
                logger);

            return next => new InlineInvoker(
                async (request, cancel) =>
                {
                    if (request.Connection == null)
                    {
                        Location location = default;
                        bool refreshCache = false;

                        if (request.Features.Get<CachedResolutionFeature>() is CachedResolutionFeature cachedResolution)
                        {
                            // This is the second (or greater) attempt, and we provided a cached resolution with the
                            // first attempt and all subsequent attempts.

                            location = cachedResolution.Location;
                            refreshCache = true;
                        }
                        else if (request.Endpoint is Endpoint locEndpoint &&
                                 locEndpoint.Transport == TransportNames.Loc)
                        {
                            // Typically first attempt since a successful resolution replaces this loc endpoint.
                            location = new Location(locEndpoint.Host);
                        }
                        else if (request.Endpoint == null && request.Protocol == Protocol.Ice1)
                        {
                            // Well-known proxy
                            location = new Location(request.Identity);
                        }

                        if (location != default)
                        {
                            try
                            {
                                (Proxy? proxy, bool fromCache) = await locationResolver.ResolveAsync(
                                    location,
                                    refreshCache,
                                    cancel).ConfigureAwait(false);

                                if (refreshCache)
                                {
                                    if (!fromCache && !request.Features.IsReadOnly)
                                    {
                                        // No need to resolve the loc endpoint / identity again since we are not
                                        // returning a cached value.
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
                            }
                            catch
                            {
                                // Ignore any exception from the location resolver. It should have been log earlier.
                            }
                        }
                    }
                    return await next.InvokeAsync(request, cancel).ConfigureAwait(false);
                });
        }

        private class CachedResolutionFeature
        {
            internal Location Location { get; }

            internal CachedResolutionFeature(Location location) => Location = location;
        }
    }
}
