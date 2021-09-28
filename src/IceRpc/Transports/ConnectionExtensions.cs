// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;

namespace IceRpc.Transports
{
    /// <summary>Provides transport related extensions methods for <see cref="Connection"/>.</summary>
    public static class ConnectionExtensions
    {
        /// <summary>Gets the network socket associated to the connection.</summary>
        /// <param name="connection">The connection.</param>
        /// <returns>The <see cref="NetworkSocket"/> or null if the connection doesn't use a network
        /// connection based on sockets.</returns>
        public static NetworkSocket? GetNetworkSocket(this Connection connection) =>
            (connection.NetworkConnection as NetworkSocketConnection)?.NetworkSocket;
    }
}
