// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc;

/// <summary>Represents a factory for client connections.</summary>
public interface IClientConnectionFactory
{
    /// <summary>Creates a client connection to the specified server address.</summary>
    /// <param name="serverAddress">The address of the server.</param>
    /// <returns>The new client connection. The caller must call <see cref="IClientConnection.ConnectAsync" /> on
    /// this connection to connect it.</returns>
    IClientConnection CreateConnection(ServerAddress serverAddress);
}
