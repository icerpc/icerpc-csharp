// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Transports
{
    /// <summary>Gives Server the ability to create incoming transport connections.</summary>
    public interface IServerTransport<T> where T : INetworkConnection
    {
        /// <summary>Starts listening on an endpoint.</summary>
        /// <param name="endpoint">The endpoint.</param>
        /// <param name="loggerFactory">A logger factory that the new listener can use to create a logger that logs
        /// internal activities. IceRpc already logs calls to all the Transports interfaces.</param>
        /// <returns>The new network connection. This connection is not yet connected.</returns>
        /// <returns>The new listener.</returns>
        /// <exception name="UnknownTransportException">Thrown if this server transport does not support the
        /// endpoint's transport.</exception>
        IListener<T> Listen(Endpoint endpoint, ILoggerFactory loggerFactory);
    }
}
