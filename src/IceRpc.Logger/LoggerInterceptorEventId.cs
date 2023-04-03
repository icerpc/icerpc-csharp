// Copyright (c) ZeroC, Inc.

namespace IceRpc.Logger;

/// <summary>This enumeration contains event ID constants used by the logger interceptor.</summary>
public enum LoggerInterceptorEventId
{
    /// <summary>The invocation returned a response with a status code that indicates if the request has completed
    /// successfully or with an error.</summary>
    InvokeResponse,

    /// <summary>The invocation failed with an exception.</summary>
    InvokeException
}
