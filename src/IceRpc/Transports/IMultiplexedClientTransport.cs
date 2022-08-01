// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports;

/// <summary>A class to create outgoing multiplexed connections.</summary>
public interface IMultiplexedClientTransport
{
    /// <summary>Gets the transport's name.</summary>
    string Name { get; }

    /// <summary>Checks if an endpoint has valid <see cref="Endpoint.Params"/> for this client transport. Only the
    /// params are included in this check.</summary>
    /// <param name="endpoint">The endpoint to check.</param>
    /// <returns><c>true</c> when all params of <paramref name="endpoint"/> are valid for this transport; otherwise,
    /// <c>false</c>.</returns>
    bool CheckParams(Endpoint endpoint);

    /// <summary>Creates a new transport connection to the specified endpoint.</summary>
    /// <param name="options">The multiplexed client connection options.</param>
    /// <returns>The new transport connection. This connection is not yet connected.</returns>
    IMultiplexedConnection CreateConnection(MultiplexedClientConnectionOptions options);
}
