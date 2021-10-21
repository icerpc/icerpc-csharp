// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Transports
{
    /// <summary>Gives Connection the ability to create outgoing transport connections.</summary>
    public interface IClientTransport<T> where T : INetworkConnection
    {
        /// <summary>Creates a new network connection to the remote endpoint.</summary>
        /// <param name="remoteEndpoint">The remote endpoint.</param>
        /// <param name="loggerFactory">A logger factory that the new connection can use to create a logger that logs
        /// internal activities. IceRpc already logs calls to all the Transports interfaces.</param>
        /// <returns>The new network connection. This connection is not yet connected.</returns>
        /// <exception name="UnknownTransportException">Thrown if this client transport does not support the remote
        /// endpoint's transport.</exception>
        T CreateConnection(Endpoint remoteEndpoint, ILoggerFactory loggerFactory);
    }
}
