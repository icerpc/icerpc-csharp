// Copyright (c) ZeroC, Inc.

namespace IceRpc.Logger;

/// <summary>This enumeration contains event ID constants used by the logger middleware.</summary>
public enum LoggerMiddlewareEventId
{
    /// <summary>The dispatch returned a response. The response's <see cref="StatusCode"/> indicates whether the
    /// dispatch of the request has completed successfully, and, if not, which error occurred.</summary>
    Dispatch,

    /// <summary>The dispatch failed with an exception.</summary>
    DispatchException,
}
