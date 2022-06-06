// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Logger;
using Microsoft.Extensions.Logging;

namespace IceRpc;

/// <summary>This class provides extension methods to add logger interceptor to a <see cref="Pipeline"/>.
/// </summary>
public static class LoggerPipelineExtensions
{
    /// <summary>Adds a <see cref="LoggerInterceptor"/> to the pipeline.</summary>
    /// <param name="pipeline">The pipeline being configured.</param>
    /// <param name="loggerFactory">The logger factory used to create the logger.</param>
    /// <returns>The pipeline being configured.</returns>
    public static Pipeline UseLogger(this Pipeline pipeline, ILoggerFactory loggerFactory) =>
        pipeline.Use(next => new LoggerInterceptor(next, loggerFactory));
}
