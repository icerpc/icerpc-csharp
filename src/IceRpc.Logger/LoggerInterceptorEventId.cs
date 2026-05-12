// Copyright (c) ZeroC, Inc.

namespace IceRpc.Logger;

/// <summary>This enumeration contains event ID constants used by the logger interceptor.</summary>
public enum LoggerInterceptorEventId
{
    /// <summary>The invocation returned a response with status code <see cref="StatusCode.Ok" />.</summary>
    Invoke,

    /// <summary>The invocation returned a response with a status code other than <see cref="StatusCode.Ok" />.
    /// </summary>
    InvokeError,

    /// <summary>The invocation failed with an exception.</summary>
    InvokeException
}
