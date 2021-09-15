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
        public static INetworkSocket? GetNetworkSocket(this Connection connection)
        {
            if (connection.NetworkConnection is NetworkSocketConnection networkSocketConnection)
            {
                return networkSocketConnection.NetworkSocket;
            }
            else if (connection.NetworkConnection is SlicConnection slicConnection)
            {
                return slicConnection.NetworkSocket;
            }
            else
            {
                return null;
            }
        }
    }
}
