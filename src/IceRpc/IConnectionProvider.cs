// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    /// <summary>Provides connections to the <see cref="Interceptors.Binder"/> interceptor.</summary>
    public interface IConnectionProvider
    {
        /// <summary>Retrieves or creates an active connection.</summary>
        /// <param name="endpoint">The main endpoint.</param>
        /// <param name="altEndpoints">A list of zero or more alternative endpoints.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>The connection.</returns>
        /// <exception cref="NoEndpointException">Thrown when none of the endpoint is usable.</exception>
        /// <exception cref="ConnectFailedException">Thrown when there is a single usable endpoint and this method
        /// failed to establish a connection to it.</exception>
        /// <exception cref="AggregateException">Thrown when there are multiple usable endpoints and this method
        /// failed to establish a connection to any of them. The <see cref="AggregateException"/> wraps the
        /// <see cref="ConnectFailedException"/> thrown by the attempts to connect to each of the usable endpoints.
        /// </exception>
        ValueTask<Connection> GetConnectionAsync(
            Endpoint endpoint,
            IEnumerable<Endpoint> altEndpoints,
            CancellationToken cancel);
    }
}
