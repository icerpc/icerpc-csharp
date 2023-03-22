// Copyright (c) ZeroC, Inc.

namespace IceRpc;

/// <summary>This enum contains event ID constants used for protocol connection related logging.</summary>
public enum ProtocolEventIds
{
    /// <summary>The protocol listener accepted a new connection.</summary>
    ConnectionAccepted,

    /// <summary>The protocol listener failed to accept a new connection. This is a critical error.</summary>
    ConnectionAcceptFailed,

    /// <summary>The protocol listener failed to accept a new connection but continues accepting more connections.
    /// </summary>
    ConnectionAcceptFailedWithRetryableException,

    /// <summary>The connection establishment completed successfully.</summary>
    ConnectionConnected,

    /// <summary>The connection establishment failed. This is the last event for this connection.</summary>
    ConnectionConnectFailed,

    /// <summary>A connected connection was disposed. This is the last event for this connection.</summary>
    ConnectionDisposed,

    /// <summary>The shutdown of a connected connection completed successfully.</summary>
    ConnectionShutdown,

    /// <summary>The shutdown of a connected connection failed.</summary>
    ConnectionShutdownFailed,

    /// <summary>The dispatch started and failed to return a response.</summary>
    DispatchFailed,

    /// <summary>The dispatch was refused before the incoming request could be read and decoded.</summary>
    DispatchRefused,

    /// <summary>The sending of the payload continuation of a request failed.</summary>
    RequestPayloadContinuationFailed,

    /// <summary>The listener starts accepting connections.</summary>
    StartAcceptingConnections,

    /// <summary>The listener stops accepting connections.</summary>
    StopAcceptingConnections,
}
