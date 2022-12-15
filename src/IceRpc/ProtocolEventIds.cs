// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc;

/// <summary>This enum contains event ID constants used for protocol connection related logging.</summary>
public enum ProtocolEventIds
{
    /// <summary>The protocol listener accepted a new connection.</summary>
    ConnectionAccepted,

    /// <summary>The protocol listener failed to accept a new connection. This is a fatal error.</summary>
    ConnectionAcceptFailed,

    /// <summary>The protocol listener failed to accept a new connection but continues accepting more connections.
    /// </summary>
    ConnectionAcceptFailedAndContinue,

    /// <summary>The connection connect attempt succeed.</summary>
    ConnectionConnected,

    /// <summary>The connection connect attempt failed.</summary>
    ConnectionConnectFailed,

    /// <summary>A connection has been terminated because a failure.</summary>
    ConnectionFailed,

    /// <summary>A connection has been shutdown.</summary>
    ConnectionShutdown,

    /// <summary>The listener starts accepting connections.</summary>
    StartAcceptingConnections,

    /// <summary>The listener stops accepting connections.</summary>
    StopAcceptingConnections,
}
