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
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="authenticationOptions">The SSL client authentication options.</param>
    /// <param name="logger">The logger created by IceRPC. IceRPC uses this logger to log calls to all Transport APIs it
    /// calls. The transport implementation can use this logger to log implementation-specific details within the log
    /// scopes created by IceRPC.</param>
    /// <returns>The new transport connection. This connection is not yet connected.</returns>
    IDuplexConnection CreateConnection(
        Endpoint endpoint,
        SslClientAuthenticationOptions? authenticationOptions,
        ILogger logger);
}
