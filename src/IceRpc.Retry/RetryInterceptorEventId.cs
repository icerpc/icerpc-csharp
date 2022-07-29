// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Retry;

/// <summary>This enumeration contains event ID constants used by the retry interceptor.</summary>
public enum RetryInterceptorEventId
{
    /// <summary>A request will be retried because of a retryable exception.</summary>
    RetryRequest,
}
