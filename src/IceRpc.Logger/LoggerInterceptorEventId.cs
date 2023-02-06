// Copyright (c) ZeroC, Inc.

namespace IceRpc.Logger;

/// <summary>This enumeration contains event ID constants used by the logger interceptor.</summary>
public enum LoggerInterceptorEventId
{
    /// <summary>The invocation was successful as far as IceRPC is concerned. Its result type can nevertheless contain
    /// a failure.</summary>
    Invoke,

    /// <summary>The invocation failed with an exception.</summary>
    InvokeException
}
