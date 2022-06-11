// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System.Net.Security;

namespace IceRpc.Transports;

/// <summary>Gives ClientConnection the ability to create outgoing transport connections.</summary>
/// <typeparam name="T">The type of the network connection used by the transport.</typeparam>
public interface IClientTransport<T> where T : INetworkConnection
{
    /// <summary>Gets the transport's name.</summary>
    string Name { get; }

    /// <summary>Checks if an endpoint has valid <see cref="Endpoint.Params"/> for this client transport. Only the
    /// params are included in this check.</summary>
    /// <param name="endpoint">The endpoint to check.</param>
    /// <returns><c>true</c> when all params of <paramref name="endpoint"/> are valid for this transport; otherwise,
    /// <c>false</c>.</returns>
    bool CheckParams(Endpoint endpoint);

    /// <summary>Creates a new network connection to the remote endpoint.</summary>
    /// <param name="remoteEndpoint">The remote endpoint.</param>
    /// <param name="authenticationOptions">The SSL client authentication options.</param>
    /// <param name="logger">The logger created by IceRPC. IceRPC uses this logger to log calls to all Transport
    /// APIs it calls. The transport implementation can use this logger to log implementation-specific details
    /// within the log scopes created by IceRPC.</param>
    /// <returns>The new network connection. This connection is not yet connected.</returns>
    T CreateConnection(
        Endpoint remoteEndpoint,
        SslClientAuthenticationOptions? authenticationOptions,
        ILogger logger);
}
