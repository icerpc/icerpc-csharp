// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Retry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IceRpc;

/// <summary>This class provides extension methods to add the retry interceptor to a <see cref="Pipeline" />.
/// </summary>
public static class RetryPipelineExtensions
{
    /// <summary>Adds a <see cref="RetryInterceptor" /> that uses the default <see cref="RetryOptions" /> to the
    /// pipeline.</summary>
    /// <param name="pipeline">The pipeline being configured.</param>
    /// <returns>The pipeline being configured.</returns>
    public static Pipeline UseRetry(this Pipeline pipeline) =>
        pipeline.UseRetry(new RetryOptions());

    /// <summary>Adds a <see cref="RetryInterceptor" /> to the pipeline.</summary>
    /// <param name="pipeline">The pipeline being configured.</param>
    /// <param name="options">The options to configure the <see cref="RetryInterceptor" />.</param>
    /// <returns>The pipeline being configured.</returns>
    public static Pipeline UseRetry(this Pipeline pipeline, RetryOptions options) =>
        pipeline.UseRetry(options, NullLogger.Instance);

    /// <summary>Adds a <see cref="RetryInterceptor" /> to the pipeline.</summary>
    /// <param name="pipeline">The pipeline being configured.</param>
    /// <param name="options">The options to configure the <see cref="RetryInterceptor" />.</param>
    /// <param name="logger">The logger.</param>
    /// <returns>The pipeline being configured.</returns>
    public static Pipeline UseRetry(this Pipeline pipeline, RetryOptions options, ILogger logger) =>
        pipeline.Use(next => new RetryInterceptor(next, options, logger));
}
