// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    /// <summary>Provides connections to the <see cref="Interceptors.Binder"/> interceptor.</summary>
    public interface IConnectionProvider
    {
        /// <summary>Retrieves or creates a connection.</summary>
        /// <param name="endpoint">The main endpoint.</param>
        /// <param name="altEndpoints">A list of zero or more alternative endpoints.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>The connection.</returns>
        /// <exception name="NoEndpointException">Thrown when none of the endpoint is usable.</exception>
        // TODO: more exceptions
        // TODO: does it need to provide an active connection?
        ValueTask<Connection> GetConnectionAsync(
            Endpoint endpoint,
            IEnumerable<Endpoint> altEndpoints,
            CancellationToken cancel);
    }
}
