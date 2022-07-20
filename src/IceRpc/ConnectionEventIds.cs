// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc;

/// <summary>Connection-related events shared by all Ice protocols.</summary>
public enum ConnectionEventIds
{
    /// <summary>The exception that triggered the closure of a connection.</summary>
    ConnectionClosedReason = Internal.BaseEventIds.Connection,

    /// <summary>The message from the connection shutdown.</summary>
    ConnectionShutdownReason,

    /// <summary>Established a protocol connection.</summary>
    ProtocolConnectionConnect,

    /// <summary>The protocol connection was shut down.</summary>
    ProtocolConnectionShutdown,

    /// <summary>The protocol connection shut down was canceled.</summary>
    ProtocolConnectionShutdownCanceled,

    /// <summary>A request was sent successfully.</summary>
    SendRequest,
}
