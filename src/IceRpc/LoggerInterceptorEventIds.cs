// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc
{
    /// <summary>This class contains event ID constants used by the logger interceptor.</summary>
    public enum LoggerInterceptorEventIds
    {
        /// <summary>The invocation of the request failed with an exception.</summary>
        InvokeException = Internal.BaseEventIds.LoggerInterceptor,
        /// <summary>Received a response.</summary>
        ReceivedResponse,
        /// <summary>A request is being sent.</summary>
        SendingRequest
    }
}
