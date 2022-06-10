// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc;

/// <summary>The state of an IceRpc connection.</summary>
public enum ConnectionState : byte
{
    /// <summary>The connection is not connected. If will be connected on the first invocation or when <see
    /// cref="ClientConnection.ConnectAsync(CancellationToken)"/> is called. A connection is in this state after creation
    /// or if it's closed and resumable.</summary>
    NotConnected,

    /// <summary>The connection establishment is in progress.</summary>
    Connecting,

    /// <summary>The connection is active and can send and receive messages.</summary>
    Active,

    /// <summary>The connection is being gracefully shutdown. If the connection is resumable and the shutdown is
    /// initiated by the peer, the connection will switch to the <see cref="NotConnected"/> state once the graceful
    /// shutdown completes. It will switch to the <see cref="Closed"/> state otherwise.</summary>
    ShuttingDown,

    /// <summary>The connection is closed and it can't be resumed.</summary>
    Closed
}
