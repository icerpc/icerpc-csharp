// Copyright (c) ZeroC, Inc.

using Microsoft.Extensions.Logging;

namespace IceRpc.Retry.Internal;

internal static partial class RetryInterceptorLoggerExtensions
{
    private static readonly Func<ILogger, int, int, IDisposable?> _retryScope =
        LoggerMessage.DefineScope<int, int>("Retry (Attempt = {Attempt}, MaxAttempts = {MaxAttempts})");

    internal static IDisposable? RetryScope(this ILogger logger, int attempt, int maxAttempts) =>
        _retryScope(logger, attempt, maxAttempts);
}
