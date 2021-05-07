// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Interop
{
    /// <summary>Resolves an identity into one or more endpoints and optionally maintains a cache for faster
    /// resolutions. Used for ice1 proxies without endpoints, known as "well-known proxies".</summary>
    public interface IIdentityResolver
    {
        /// <summary>Resolves an identity</summary>
        /// <param name="identity">The identity to resolve.</param>
        /// <param name="refreshCache">When true, the caller requests a cache refresh.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>The resolved endpoints, or null plus an empty list if the endpoint could not be resolved.</returns>
        /// <seealso cref="ILocationResolver"/>
        ValueTask<(Endpoint? Endpoint, ImmutableList<Endpoint> AltEndpoints)> ResolveAsync(
            Identity identity,
            bool refreshCache,
            CancellationToken cancel);
    }
}
