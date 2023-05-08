// Copyright (c) ZeroC, Inc.

namespace IceRpc.Logger;

/// <summary>This enumeration contains event ID constants used by the logger interceptor.</summary>
public enum LoggerInterceptorEventId
{
    /// <summary>The invocation returned a response. The response's <see cref="StatusCode"/> indicates whether the
    /// dispatch of the request has completed successfully, and, if not, which error occurred.</summary>
    Invoke,

    /// <summary>The invocation failed with an exception.</summary>
    InvokeException
}
