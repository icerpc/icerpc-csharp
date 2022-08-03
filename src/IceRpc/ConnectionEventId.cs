// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc;

/// <summary>Connection-related events.</summary>
public enum ConnectionEventId
{
    /// <summary>The connect operation for an ice or icerpc connection completed successfully.</summary>
    Connect = Internal.BaseEventId.Connection,

    /// <summary>The connect operation for an ice or icerpc connection failed with an exception.</summary>
    ConnectException,

    /// <summary>An ice or icerpc connection was disposed.</summary>
    Dispose,

    /// <summary>The shutdown operation for an ice or icerpc connection completed successfully.</summary>
    Shutdown,

    /// <summary>The shutdown operation for an ice or icerpc connection failed with an exception.</summary>
    ShutdownException,
}
