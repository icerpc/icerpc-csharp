// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc;

/// <summary>Connection-related events.</summary>
public enum ConnectionEventIds
{
    /// <summary>The connect operation for an ice or icerpc connection completed successfully.</summary>
    Connect = Internal.BaseEventIds.Connection,

    /// <summary>The connect operation for an ice or icerpc connection failed with an exception.</summary>
    ConnectException,

    /// <summary>A dispatch started by a connection completed.</summary>
    Dispatch,

    /// <summary>A dispatch started by a connection failed with an exception.</summary>
    DispatchException,

    /// <summary>An ice or icerpc connection was disposed.</summary>
    Dispose,

    /// <summary>An invocation completed.</summary>
    Invoke,

    /// <summary>An invocation failed with an exception.</summary>
    InvokeException,

    /// <summary>The shutdown operation for an ice or icerpc connection completed successfully.</summary>
    Shutdown,

    /// <summary>The shutdown operation for an ice or icerpc connection failed with an exception.</summary>
    ShutdownException,
}
