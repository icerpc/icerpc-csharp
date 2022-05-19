// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Timeout;

namespace IceRpc.Configure;

/// <summary>This class provide extension methods to add the timeout interceptor to a <see cref="Pipeline"/>
/// </summary>
public static class TimeoutPipelineExtensions
{
    /// <summary>Adds a <see cref="TimeoutInterceptor"/> to the pipeline.</summary>
    /// <param name="pipeline">The pipeline being configured.</param>
    /// <param name="timeout">The timeout for the invocation.</param>
    /// <returns>The pipeline being configured.</returns>
    public static Pipeline UseTimeout(this Pipeline pipeline, TimeSpan timeout) =>
        pipeline.Use(next => new TimeoutInterceptor(next, timeout));
}
