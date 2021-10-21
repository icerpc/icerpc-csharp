// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports
{
    /// <summary>Gives Connection the ability to create outgoing transport connections.</summary>
    public interface IClientTransport<T> where T : INetworkConnection
    {
        /// <summary>Creates a new network connection to the remote endpoint.</summary>
        /// <param name="remoteEndpoint">The remote endpoint.</param>
        /// <returns>The new network connection. This connection is not yet connected.</returns>
        /// <exception name="UnknownTransportException">Thrown if this client transport does not support the remote
        /// endpoint's transport.</exception>
        T CreateConnection(Endpoint remoteEndpoint);
    }
}
