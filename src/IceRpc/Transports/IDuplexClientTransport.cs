// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System.Net.Security;

namespace IceRpc.Transports;

/// <summary>A class to create outgoing duplex connections.</summary>
public interface IDuplexClientTransport
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
    /// <param name="endpoint">The endpoint of the new connection it corresponds to the address of the server-end of
    /// that connection.</param>
    /// <param name="options">The duplex connection options.</param>
    /// <param name="clientAuthenticationOptions">The SSL client authentication options.</param>
    /// <returns>The new transport connection. This connection is not yet connected.</returns>
    IDuplexConnection CreateConnection(
        Endpoint endpoint,
        DuplexConnectionOptions options,
        SslClientAuthenticationOptions? clientAuthenticationOptions);
}
