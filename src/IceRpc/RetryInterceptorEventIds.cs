// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc
{
    /// <summary>This class contains event ID constants used by the retry interceptor.</summary>
    public enum RetryInterceptorEventIds
    {
        /// <summary>A request will be retried because of a retryable exception.</summary>
        RetryRequest = Internal.BaseEventIds.Retry,
    }
}
