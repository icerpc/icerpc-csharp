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
    /// <summary>Provides the implementation of
    /// <see cref="Interceptors.Locator(ILocatorPrx, Interceptors.LocatorOptions)"/>.</summary>
    internal sealed class LocatorClient
    {
        private readonly ILocationResolver _locationResolver;

        /// <summary>Constructs a locator invoker.</summary>
        internal LocatorClient(ILocatorPrx locator, Interceptors.LocatorOptions options)
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

            _locationResolver = new LocationResolver(endpointFinder,
                                                     endpointCache,
                                                     options.Background,
                                                     options.JustRefreshedAge,
                                                     options.Ttl);
        }

        /// <summary>Updates the endpoints of the request (as appropriate) then call InvokeAsync on next.</summary>
        /// <param name="request">The outgoing request.</param>
        /// <param name="next">The next invoker in the pipeline.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>The response.</returns>
        internal async Task<IncomingResponse> InvokeAsync(
            OutgoingRequest request,
            IInvoker next,
            CancellationToken cancel)
        {
            if (request.Connection == null)
            {
                Location location = default;
                bool refreshCache = false;

                if (request.Features.Get<CachedResolutionFeature>() is CachedResolutionFeature cachedResolution)
                {
                    // This is the second (or greater) attempt, and we provided a cached resolution with the first
                    // attempt and all subsequent attempts.

                    location = cachedResolution.Location;
                    refreshCache = true;
                }
                else if (request.Endpoint is Endpoint locEndpoint && locEndpoint.Transport == TransportNames.Loc)
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
                        (Proxy? proxy, bool fromCache) =
                            await _locationResolver.ResolveAsync(location, refreshCache, cancel).ConfigureAwait(false);

                        if (refreshCache)
                        {
                            if (!fromCache && !request.Features.IsReadOnly)
                            {
                                // No need to resolve the loc endpoint / identity again since we are not returning a
                                // cached value.
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

                        if (proxy?.Endpoint != null)
                        {
                            request.Endpoint = proxy.Endpoint;
                            request.AltEndpoints = proxy.AltEndpoints;
                        }
                    }
                    catch
                    {
                    }
                }
            }

            return await next.InvokeAsync(request, cancel).ConfigureAwait(false);
        }

        private sealed class CachedResolutionFeature
        {
            internal Location Location { get; }

            internal CachedResolutionFeature(Location location) => Location = location;
        }
    }
}
