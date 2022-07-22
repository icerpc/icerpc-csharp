// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc;

/// <summary>Connection-related events shared by all Ice protocols.</summary>
public enum ConnectionEventIds
{
    /// <summary>The exception that triggered the closure of a connection.</summary>
    ConnectionClosedReason = Internal.BaseEventIds.Connection,

    /// <summary>The message from the connection shutdown.</summary>
    ConnectionShutdownReason,

    /// <summary>The connect operation for an ice or icerpc connection completed successfully.</summary>
    Connect,

    /// <summary>The connect operation for an ice or icerpc connection failed with an exception.</summary>
    ConnectException,

    /// <summary>An ice or icerpc connection was disposed.</summary>
    Dispose,

    /// <summary>The shutdown operation for an ice or icerpc connection completed successfully.</summary>
    Shutdown,

    /// <summary>The shutdown operation for an ice or icerpc connection failed with an exception.</summary>
    ShutdownException,

    /// <summary>The protocol connection was shut down.</summary>
    ProtocolConnectionShutdown,

    /// <summary>The protocol connection shut down was canceled.</summary>
    ProtocolConnectionShutdownCanceled,

    /// <summary>A request was sent successfully.</summary>
    SendRequest,
}
