// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Transports
{
    /// <summary>Gives class <see cref="Server"/> the ability to create incoming transport connections.</summary>
    public interface IServerTransport<T> where T : INetworkConnection
    {
        /// <summary>The default listener endpoint.</summary>
        Endpoint DefaultEndpoint { get; }

        /// <summary>Starts listening on an endpoint.</summary>
        /// <param name="endpoint">The endpoint.</param>
        /// <param name="logger">The logger created by IceRPC. IceRPC uses this logger to log calls to all Transport
        /// APIs it calls. The transport implementation can use this logger to log implementation-specific details
        /// within the log scopes created by IceRPC.</param>
        /// <returns>The new network connection. This connection is not yet connected.</returns>
        /// <returns>The new listener.</returns>
        /// <exception cref="UnknownTransportException">Thrown if this server transport does not support the endpoint's
        /// transport.</exception>
        IListener<T> Listen(Endpoint endpoint, ILogger logger);
    }
}
