// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports;

/// <summary>This enum contains event ID constants used for protocol logging.</summary>
public enum ProtocolEventIds
{
    /// <summary>The protocol listener accepted a new connection.</summary>
    AcceptConnectionSucceed,

    /// <summary>The protocol listener failed to accept a new connection.</summary>
    AcceptConnectionFailed,

    /// <summary>A client connection has been created.</summary>
    ConnectionCreated,

    /// <summary>A server connection has been terminated because a failure.</summary>
    ConnectionFailure,

    /// <summary>A server connection has been shutdown.</summary>
    ConnectionShutdown,

    /// <summary>The server connection connect attempt failed.</summary>
    ConnectFailed,

    /// <summary>The server connection connect attempt succeed.</summary>
    ConnectSucceed,

    /// <summary>The listener starts accepting connections.</summary>
    StartAcceptingConnections,

    /// <summary>The listener stops accepting connections.</summary>
    StopAcceptingConnections,
}
