// Copyright (c) ZeroC, Inc.

using IceRpc.Logger;
using Microsoft.Extensions.Logging;

namespace IceRpc;

/// <summary>Provides an extension method to add a logger interceptor to a <see cref="Pipeline" />.</summary>
public static class LoggerPipelineExtensions
{
    /// <summary>Adds a <see cref="LoggerInterceptor" /> to this pipeline.</summary>
    /// <param name="pipeline">The pipeline being configured.</param>
    /// <param name="loggerFactory">The logger factory used to create a <see cref="ILogger{TCategoryName}" /> for
    /// <see cref="LoggerInterceptor" />.</param>
    /// <returns>The pipeline being configured.</returns>
    public static Pipeline UseLogger(this Pipeline pipeline, ILoggerFactory loggerFactory) =>
        pipeline.Use(next => new LoggerInterceptor(next, loggerFactory.CreateLogger<LoggerInterceptor>()));
}
