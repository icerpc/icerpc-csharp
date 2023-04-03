// Copyright (c) ZeroC, Inc.

namespace IceRpc.Logger;

/// <summary>This enumeration contains event ID constants used by the logger middleware.</summary>
public enum LoggerMiddlewareEventId
{
    /// <summary>The dispatch returned a response with a status code that indicates if the dispatch of the request has
    /// completed successfully or with an error.</summary>
    Dispatch,

    /// <summary>The dispatch failed with an exception.</summary>
    DispatchException,
}
