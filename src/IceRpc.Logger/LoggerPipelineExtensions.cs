// Copyright (c) ZeroC, Inc.

using IceRpc.Logger;
using Microsoft.Extensions.Logging;

namespace IceRpc;

/// <summary>This class provides extension methods to add the logger interceptor to a <see cref="Pipeline" />.
/// </summary>
public static class LoggerPipelineExtensions
{
    /// <summary>Adds a <see cref="LoggerInterceptor" /> to the pipeline.</summary>
    /// <param name="pipeline">The pipeline being configured.</param>
    /// <param name="logger">The logger to log to.</param>
    /// <returns>The pipeline being configured.</returns>
    public static Pipeline UseLogger(this Pipeline pipeline, ILogger logger) =>
        pipeline.Use(next => new LoggerInterceptor(next, logger));

    /// <summary>Adds a <see cref="LoggerInterceptor" /> to the pipeline.</summary>
    /// <param name="pipeline">The pipeline being configured.</param>
    /// <param name="loggerFactory">The logger factory used to create a <see cref="ILogger{TCategoryName}" /> for
    /// <see cref="LoggerInterceptor" />.</param>
    /// <returns>The pipeline being configured.</returns>
    public static Pipeline UseLogger(this Pipeline pipeline, ILoggerFactory loggerFactory) =>
        pipeline.UseLogger(loggerFactory.CreateLogger<LoggerInterceptor>());
}
