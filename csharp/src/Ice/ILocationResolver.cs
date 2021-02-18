// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;

using EndpointList = System.Collections.Generic.IReadOnlyList<ZeroC.Ice.Endpoint>;

namespace ZeroC.Ice
{
    /// <summary>A location resolver retrieves the endpoints of indirect proxies and optionally maintains a cache for
    /// faster resolutions.</summary>
    public interface ILocationResolver
    {
        /// <summary>Clears the cache entry (if any) of this well-known proxy.</summary>
        /// <param name="wellKnownProxy">The well-known proxy.</param>
        void ClearCache(ObjectPrx wellKnownProxy);

        /// <summary>Retrieves the endpoint(s) of an indirect proxy.</summary>
        /// <param name="proxy">The indirect proxy to resolve.</param>
        /// <param name="endpointsMaxAge">TBD.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>A value task holding the resolved endpoint list and TBD. When the proxy cannot be resolved, the
        /// endpoint list is empty.</returns>
        ValueTask<(EndpointList Endpoints, TimeSpan EndpointsAge)> ResolveIndirectProxyAsync(
            ObjectPrx proxy,
            TimeSpan endpointsMaxAge,
            CancellationToken cancel);
    }
}
