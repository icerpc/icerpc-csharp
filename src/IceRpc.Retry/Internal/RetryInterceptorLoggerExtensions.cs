// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Retry.Internal;

internal static partial class RetryInterceptorLoggerExtensions
{
    private static readonly Func<ILogger, int, int, RetryPolicy, IDisposable> _retryScope =
        LoggerMessage.DefineScope<int, int, RetryPolicy>(
            "Retry (Attempt = {Attempt}, MaxAttempts = {MaxAttempts}, RetryPolicy = {RetryPolicy})");

    internal static IDisposable RetryScope(
        this ILogger
        logger,
        int attempt,
        int maxAttempts,
        RetryPolicy retryPolicy) =>
        _retryScope(logger, attempt, maxAttempts, retryPolicy);
}
