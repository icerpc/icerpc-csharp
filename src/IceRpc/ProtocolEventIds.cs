// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports;

/// <summary>This enum contains event ID constants used for protocol connection related logging.</summary>
public enum ProtocolEventIds
{
    /// <summary>The protocol listener accepted a new connection.</summary>
    ConnectionAccepted,

    /// <summary>The protocol listener failed to accept a new connection.</summary>
    ConnectionAcceptFailed,

    /// <summary>The connection connect attempt succeed.</summary>
    ConnectionConnected,

    /// <summary>The connection connect attempt failed.</summary>
    ConnectionConnectFailed,

    /// <summary>A client connection has been created.</summary>
    ConnectionCreated,

    /// <summary>A connection has been terminated because a failure.</summary>
    ConnectionFailed,

    /// <summary>A request dispatch failed due to an internal error.</summary>
    ConnectionInternalDispatchFailure,

    /// <summary>A request dispatch failed due to an protocol error.</summary>
    ConnectionProtocolDispatchFailure,

    /// <summary>Received an invalid request that we cannot decode at it will be ignored.</summary>
    ConnectionReceivedInvalidRequest,

    /// <summary>A connection has been shutdown.</summary>
    ConnectionShutdown,

    /// <summary>The listener starts accepting connections.</summary>
    StartAcceptingConnections,

    /// <summary>The listener stops accepting connections.</summary>
    StopAcceptingConnections,
}
