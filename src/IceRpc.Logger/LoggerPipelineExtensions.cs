// Copyright (c) ZeroC, Inc.

using IceRpc.Logger;
using Microsoft.Extensions.Logging;

namespace IceRpc;

/// <summary>Provides an extension method for <see cref="Pipeline" /> to add the logger interceptor.</summary>
public static class LoggerPipelineExtensions
{
    /// <summary>Adds a <see cref="LoggerInterceptor" /> to this pipeline.</summary>
    /// <param name="pipeline">The pipeline being configured.</param>
    /// <param name="loggerFactory">The logger factory used to create a <see cref="ILogger{TCategoryName}" /> for
    /// <see cref="LoggerInterceptor" />.</param>
    /// <returns>The pipeline being configured.</returns>
    /// <example>
    /// The following code adds the logger interceptor to the invocation pipeline.
    /// <code source="../../docfx/examples/IceRpc.Logger.Examples/LoggerInterceptorExamples.cs" region="UseLogger" lang="csharp" />
    /// </example>
    /// <seealso href="https://github.com/icerpc/icerpc-csharp/tree/0.1.x/examples/GreeterLog"/>
    public static Pipeline UseLogger(this Pipeline pipeline, ILoggerFactory loggerFactory) =>
        pipeline.Use(next => new LoggerInterceptor(next, loggerFactory.CreateLogger<LoggerInterceptor>()));
}
