// Copyright (c) ZeroC, Inc.

namespace IceRpc.Logger;

/// <summary>This enumeration contains event ID constants used by the logger middleware.</summary>
public enum LoggerMiddlewareEventId
{
    /// <summary>The dispatch returned a response with status code <see cref="StatusCode.Ok" />.</summary>
    Dispatch,

    /// <summary>The dispatch returned a response with a status code other than <see cref="StatusCode.Ok" />.
    /// </summary>
    DispatchError,

    /// <summary>The dispatch failed with an exception.</summary>
    DispatchException,
}
