// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Retry;

namespace IceRpc.Configure;

/// <summary>This class provide extension methods to add metrics interceptors to a <see cref="Pipeline"/>.
/// </summary>
public static class PipelineExtensions
{
    /// <summary>Adds a <see cref="RetryInterceptor"/> that use the default <see cref="RetryOptions"/> to the
    /// pipeline.</summary>
    /// <param name="pipeline">The pipeline being configured.</param>
    /// <returns>The pipeline being configured.</returns>
    public static Pipeline UseRetry(this Pipeline pipeline) =>
        pipeline.UseRetry(new RetryOptions());

    /// <summary>Adds a <see cref="RetryInterceptor"/> to the pipeline.</summary>
    /// <param name="pipeline">The pipeline being configured.</param>
    /// <param name="options">The options to configure the <see cref="RetryInterceptor"/>.</param>
    /// <returns>The pipeline being configured.</returns>
    public static Pipeline UseRetry(this Pipeline pipeline, RetryOptions options) =>
        pipeline.Use(next => new RetryInterceptor(next, options));
}
