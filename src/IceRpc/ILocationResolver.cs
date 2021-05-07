// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    /// <summary>Resolves a loc endpoint into one or more endpoints and optionally maintains a cache for faster
    /// resolutions.</summary>
    public interface ILocationResolver
    {
        /// <summary>Resolves a location</summary>
        /// <param name="locEndpoint">An endpoint with the loc pseudo transport.</param>
        /// <param name="refreshCache">When true, the caller requests a cache refresh.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>The resolved endpoints, or null plus an empty list if the endpoint could not be resolved.</returns>
        ValueTask<(Endpoint? Endpoint, ImmutableList<Endpoint> AltEndpoints)> ResolveAsync(
            Endpoint locEndpoint,
            bool refreshCache,
            CancellationToken cancel);
    }
}
