// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System.Net.Security;

namespace IceRpc.Transports
{
    /// <summary>Gives class <see cref="Server"/> the ability to create incoming transport connections.</summary>
    public interface IServerTransport<T> where T : INetworkConnection
    {
        /// <summary>Returns the transport's name.</summary>
        string Name { get; }

        /// <summary>Starts listening on an endpoint.</summary>
        /// <param name="endpoint">The endpoint.</param>
        /// <param name="authenticationOptions">The SSL server authentication options.</param>
        /// <param name="logger">The logger created by IceRPC. IceRPC uses this logger to log calls to all Transport
        /// APIs it calls. The transport implementation can use this logger to log implementation-specific details
        /// within the log scopes created by IceRPC.</param>
        /// <returns>The new network connection. This connection is not yet connected.</returns>
        /// <returns>The new listener.</returns>
        /// <exception cref="UnknownTransportException">Thrown if this server transport does not support the endpoint's
        /// transport.</exception>
        IListener<T> Listen(Endpoint endpoint, SslServerAuthenticationOptions? authenticationOptions, ILogger logger);
    }
}
