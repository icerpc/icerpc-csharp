// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Retry.Internal;

internal static partial class RetryInterceptorLoggerExtensions
{
    internal static void LogRetryRequest(
        this ILogger logger,
        IConnection? connection,
        string path,
        string operation,
        RetryPolicy retryPolicy,
        int attempt,
        int maxAttempts,
        Exception? ex)
    {
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogRetryRequest(
                connection?.NetworkConnectionInformation?.LocalNetworkAddress?.ToString() ?? "undefined",
                connection?.NetworkConnectionInformation?.RemoteNetworkAddress?.ToString() ?? "undefined",
                path,
                operation,
                retryPolicy,
                attempt,
                maxAttempts,
                ex);
        }
    }

    [LoggerMessage(
        EventId = (int)RetryInterceptorEventIds.RetryRequest,
        EventName = nameof(RetryInterceptorEventIds.RetryRequest),
        Level = LogLevel.Information,
        Message = "retrying request because of retryable exception (LocalNetworkAddress={LocalNetworkAddress}, " +
                  "RemoteNetworkAddress={RemoteNetworkAddress}, Path={Path}, Operation={Operation}, " +
                  "RetryPolicy={RetryPolicy}, Attempt={Attempt}/{MaxAttempts})")]
    private static partial void LogRetryRequest(
        this ILogger logger,
        string localNetworkAddress,
        string remoteNetworkAddress,
        string path,
        string operation,
        RetryPolicy retryPolicy,
        int attempt,
        int maxAttempts,
        Exception? ex);
}
