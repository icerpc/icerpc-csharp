// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Logger;

/// <summary>This class contains event ID constants used by the logger interceptor.</summary>
public enum LoggerInterceptorEventIds
{
    /// <summary>The invocation of the request failed with an exception.</summary>
    InvokeException,
    /// <summary>Received a response.</summary>
    ReceivedResponse,
    /// <summary>A request is being sent.</summary>
    SendingRequest
}
