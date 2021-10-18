// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Transports
{
    /// <summary>Gives Servers the ability to create server network connections.</summary>
    public interface IServerTransport
    {
        /// <summary>Starts listening on an endpoint.</summary>
        /// <param name="endpoint">The endpoint.</param>
        /// <returns>The new listener.</returns>
        /// <exception name="UnknownTransportException">Thrown if this server transport does not support the
        /// endpoint's transport.</exception>
        IListener Listen(Endpoint endpoint);
    }
}
