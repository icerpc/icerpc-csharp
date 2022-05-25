// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc;

/// <summary>Provides client connections based on the given endpoints.</summary>
public interface IClientConnectionProvider
{
    /// <summary>Retrieves or creates a client connection.</summary>
    /// <param name="endpoint">The main endpoint.</param>
    /// <param name="altEndpoints">A list of zero or more alternative endpoints.</param>
    /// <param name="cancel">The cancellation token.</param>
    /// <returns>The connection.</returns>
    /// <exception cref="NoEndpointException">Thrown when none of the endpoint is usable.</exception>
    /// <exception cref="ConnectFailedException">Thrown when there is a single usable endpoint and this method failed to
    /// establish a connection to this endpoint.</exception>
    /// <exception cref="AggregateException">Thrown when there are multiple usable endpoints and this method failed to
    /// establish a connection to any of them. The <see cref="AggregateException"/> wraps the
    /// <see cref="ConnectFailedException"/> thrown by the attempts to connect to each of the usable endpoints.
    /// </exception>
    ValueTask<IClientConnection> GetClientConnectionAsync(
        Endpoint endpoint,
        IEnumerable<Endpoint> altEndpoints,
        CancellationToken cancel);
}
