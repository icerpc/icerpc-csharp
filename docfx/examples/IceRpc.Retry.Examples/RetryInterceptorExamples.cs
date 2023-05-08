// Copyright (c) ZeroC, Inc.

using Microsoft.Extensions.Logging;

namespace IceRpc.Retry.Examples;

// This class provides code snippets used by the doc-comments of the retry interceptor.
public static class RetryInterceptorExamples
{
    public static async Task UseRetry()
    {
        #region UseRetry
        // Create a connection cache.
        await using var connectionCache = new ConnectionCache();

        // Create an invocation pipeline and install the retry interceptor.
        Pipeline pipeline = new Pipeline()
            .UseRetry()
            .Into(connectionCache);
        #endregion
    }

    public static async Task UseRetryWithOptions()
    {
        #region UseRetryWithOptions
        // Create a connection cache.
        await using var connectionCache = new ConnectionCache();

        // Create an invocation pipeline and install the retry interceptor.
        Pipeline pipeline = new Pipeline()
            .UseRetry(new RetryOptions
            {
                MaxAttempts = 5, // Make up to 5 attempts before giving up.
                MaxPayloadSize = 3 * 1024, // 3 MB
            })
            .Into(connectionCache);
        #endregion
    }

    public static async Task UseRetryWithLoggerFactory()
    {
        #region UseRetryWithLoggerFactory
        // Create a connection cache.
        await using var connectionCache = new ConnectionCache();

        // Create a simple console logger factory and configure the log level for category IceRpc.
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
            builder
                .AddSimpleConsole()
                .AddFilter("IceRpc", LogLevel.Debug));

        // Create an invocation pipeline with the retry and logger interceptors.
        Pipeline pipeline = new Pipeline()
            .UseRetry(
                // Make up to 5 attempts before giving up.
                new RetryOptions { MaxAttempts = 5 },
                loggerFactory)
            .UseLogger(loggerFactory)
            .Into(connectionCache);
        #endregion
    }
}
