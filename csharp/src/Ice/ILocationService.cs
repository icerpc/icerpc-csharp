// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;

using EndpointList = System.Collections.Generic.IReadOnlyList<ZeroC.Ice.Endpoint>;
using Location = System.Collections.Generic.IReadOnlyList<string>;

namespace ZeroC.Ice
{
    /// <summary>A location service retrieves the endpoints of indirect proxies and optionally maintains a cache for
    /// faster resolutions.</summary>
    public interface ILocationService
    {
        /// <summary>Retrieves the endpoint(s) for a location.</summary>
        /// <param name="location">The location.</param>
        /// <param name="protocol">The protocol.</param>
        /// <param name="endpointsMaxAge">The maximum age of the cache entry.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>A value task holding the resolved endpoint(s) and the age of the cache entry. When the location
        /// cannot be resolved, the endpoint list is empty.</returns>
        ValueTask<(EndpointList Endpoints, TimeSpan EndpointsAge)> ResolveLocationAsync(
            Location location,
            Protocol protocol,
            TimeSpan endpointsMaxAge,
            CancellationToken cancel);

        /// <summary>Retrieves the endpoint(s) for a well-known proxy.</summary>
        /// <param name="identity">The well-known proxy's identity.</param>
        /// <param name="facet">The well-known proxy's facet.</param>
        /// <param name="protocol">The protocol.</param>
        /// <param name="endpointsMaxAge">The maximum age of the cache entry.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>A value task holding the resolved endpoint(s) and the age of the cache entry. When the identity
        /// and facet cannot be resolved, the endpoint list is empty.</returns>
        ValueTask<(EndpointList Endpoints, TimeSpan EndpointsAge)> ResolveWellKnownProxyAsync(
            Identity identity,
            string facet,
            Protocol protocol,
            TimeSpan endpointsMaxAge,
            CancellationToken cancel);
    }
}
