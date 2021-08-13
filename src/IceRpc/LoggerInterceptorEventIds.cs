// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc
{
    /// <summary>This class contains event ID constants used by the logger interceptor.</summary>
    public enum LoggerInterceptorEventIds
    {
        /// <summary>Received a response.</summary>
        ReceivedResponse = Internal.BaseEventIds.LoggerInterceptor,
        /// <summary>A request is being sent.</summary>
        SendingRequest,
        /// <summary>A request failed with an exception.</summary>
        RequestException
    }
}
