// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.RequestContext;

namespace IceRpc.Configure;

/// <summary>This class provide extension methods to add request context interceptor to a <see cref="Pipeline"/>
/// </summary>
public static class MetricsPipelineExtensions
{
    /// <summary>Adds a <see cref="RequestContext"/> to the pipeline.</summary>
    /// <param name="pipeline">The pipeline being configured.</param>
    /// <returns>The pipeline being configured.</returns>
    public static Pipeline UseRequestContext(this Pipeline pipeline) =>
        pipeline.Use(next => new RequestContextInterceptor(next));
}
