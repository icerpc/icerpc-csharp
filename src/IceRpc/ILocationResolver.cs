// Copyright (c) ZeroC, Inc. All rights reserved.

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
        /// <param name="refreshCache">When true, the caller requests a cache refresh.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>A value task holding the resolved endpoint(s). When a loc endpoint cannot be resolved, the endpoint
        /// list is empty.</returns>
        ValueTask<IReadOnlyList<Endpoint>> ResolveAsync(Endpoint endpoint, bool refreshCache, CancellationToken cancel);
    }
}
