// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Transports
{
    /// <summary>Gives Servers the ability to create incoming transport connections.</summary>
    public interface IServerTransport
    {
        /// <summary>Starts listening on an endpoint.</summary>
        /// <param name="endpoint">The endpoint.</param>
        /// <param name="options">The connection options.</param>
        /// <param name="logger">The logger.</param>
        /// <returns>Either a new listener or a server connection, depending on the transport of
        /// <paramref name="endpoint"/>.</returns>
        /// <exception name="UnknownTransportException">Thrown if this server transport does not support the endpoint's
        /// transport.</exception>
        (IListener? Listener, MultiStreamConnection? Connection) Listen(
            EndpointRecord endpoint,
            ServerConnectionOptions options,
            ILogger logger);
    }
}
