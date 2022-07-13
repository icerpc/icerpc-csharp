// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Retry.Internal;

internal static partial class RetryInterceptorLoggerExtensions
{
    [LoggerMessage(
        EventId = (int)RetryInterceptorEventIds.RetryRequest,
        EventName = nameof(RetryInterceptorEventIds.RetryRequest),
        Level = LogLevel.Information,
        Message = "retrying request because of retryable exception (Path={Path}, Operation={Operation}, " +
                  "RetryPolicy={RetryPolicy}, Attempt={Attempt}/{MaxAttempts})")]
    internal static partial void LogRetryRequest(
        this ILogger logger,
        string path,
        string operation,
        RetryPolicy retryPolicy,
        int attempt,
        int maxAttempts,
        Exception? ex);
}
