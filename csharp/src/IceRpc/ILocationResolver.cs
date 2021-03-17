// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Interop;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace IceRpc
{
    /// <summary>A location resolver resolves endpoints with the loc transport into other endpoints and optionally
    /// maintains a cache for faster resolutions.</summary>
    public interface ILocationResolver
    {
        /// <summary>Resolves an endpoint with the loc transport.</summary>
        /// <param name="endpoint">The input endpoint.</param>
        /// <param name="endpointsMaxAge">The maximum age of the cache entry.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>A value task holding the resolved endpoint(s) and the age of the cache entry. When a loc endpoint
        /// cannot be resolved, the endpoint list is empty.</returns>
        ValueTask<(IReadOnlyList<Endpoint> Endpoints, TimeSpan EndpointsAge)> ResolveAsync(
            Endpoint endpoint,
            TimeSpan endpointsMaxAge,
            CancellationToken cancel);
    }
}
