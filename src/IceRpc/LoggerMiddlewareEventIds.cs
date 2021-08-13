// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc
{
    /// <summary>This class contains event ID constants used by the logger middleware.</summary>
    public enum LoggerMiddlewareEventIds
    {
        /// <summary>Received a request.</summary>
        ReceivedRequest = Internal.BaseEventIds.LoggerMiddleware,
        /// <summary>A response is being sent.</summary>
        SendingResponse
    }
}
