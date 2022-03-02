// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System.Net.Security;

namespace IceRpc.Transports
{
    /// <summary>Gives Connection the ability to create outgoing transport connections.</summary>
    public interface IClientTransport<T> where T : INetworkConnection
    {
        /// <summary>Creates a new network connection to the remote endpoint.</summary>
        /// <param name="remoteEndpoint">The remote endpoint.</param>
        /// <param name="authenticationOptions">The SSL client authentication options.</param>
        /// <param name="logger">The logger created by IceRPC. IceRPC uses this logger to log calls to all Transport
        /// APIs it calls. The transport implementation can use this logger to log implementation-specific details
        /// within the log scopes created by IceRPC.</param>
        /// <returns>The new network connection. This connection is not yet connected.</returns>
        /// <exception cref="UnknownTransportException">Thrown if this client transport does not support the remote
        /// endpoint's transport.</exception>
        T CreateConnection(
            Endpoint remoteEndpoint,
            SslClientAuthenticationOptions? authenticationOptions,
            ILogger logger);
    }
}
