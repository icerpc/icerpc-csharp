// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports;

/// <summary>A multiplexed listener listens for connection requests from clients. It creates a multiplexed connection
/// when it accepts a connection from a client.</summary>
public interface IMultiplexedListener : IDisposable
{
    /// <summary>Gets the server address this listener is listening on. This server address can be different from the server address used
    /// to create the listener if for example the binding of the server socket assigned a port.</summary>
    ServerAddress ServerAddress { get; }

    /// <summary>Accepts a new multiplexed connection.</summary>
    /// <returns>The accepted multiplexed connection.</returns>
    Task<IMultiplexedConnection> AcceptAsync();
}
