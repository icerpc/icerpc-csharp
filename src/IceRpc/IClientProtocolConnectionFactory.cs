// Copyright (c) ZeroC, Inc.

namespace IceRpc;

/// <summary>Represents a factory for client protocol connections.</summary>
public interface IClientProtocolConnectionFactory
{
    /// <summary>Creates a protocol connection to the specified server address.</summary>
    /// <param name="serverAddress">The address of the server.</param>
    /// <returns>The new protocol connection. The caller must call <see cref="IProtocolConnection.ConnectAsync" /> on
    /// this connection to connect it.</returns>
    IProtocolConnection CreateConnection(ServerAddress serverAddress);
}
