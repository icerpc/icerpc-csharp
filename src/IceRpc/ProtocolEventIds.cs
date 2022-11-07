// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports;

/// <summary>This enum contains event ID constants used for protocol logging.</summary>
public enum ProtocolEventIds
{
    /// <summary>The protocol listener accepted a new connection.</summary>
    AcceptConnection = 1,

    /// <summary>The protocol listener failed to accept a new connection.</summary>
    AcceptConnectionFailed,

    /// <summary>A client connection has been created.</summary>
    ConnectionCreated,

    /// <summary>A client connection has been terminated because a failure.</summary>
    ClientConnectionFailure,

    /// <summary>A client connection has been shutdown.</summary>
    ClientConnectionShutdown,

    /// <summary>The client connection connect attempt failed.</summary>
    ClientConnectFailed,

    /// <summary>The client connection connect attempt succeed.</summary>
    ClientConnectSucceed,

    /// <summary>A server connection has been terminated because a failure.</summary>
    ServerConnectionFailure,

    /// <summary>A server connection has been shutdown.</summary>
    ServerConnectionShutdown,

    /// <summary>The server connection connect attempt failed.</summary>
    ServerConnectFailed,

    /// <summary>The server connection connect attempt succeed.</summary>
    ServerConnectSucceed,

    /// <summary>The listener starts accepting connections.</summary>
    StartAcceptingConnections,

    /// <summary>The listener stops accepting connections.</summary>
    StopAcceptingConnections,
}
