// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Logger;

/// <summary>This class contains event ID constants used by the logger middleware.</summary>
public enum LoggerMiddlewareEventIds
{
    /// <summary>The dispatch of the request failed.</summary>
    DispatchException,
    /// <summary>Received a request.</summary>
    ReceivedRequest,
    /// <summary>A response is being sent.</summary>
    SendingResponse
}
