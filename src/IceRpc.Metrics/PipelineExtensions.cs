// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Metrics;

namespace IceRpc.Configure;

/// <summary>This class provide extension methods to add metrics interceptor to a <see cref="Pipeline"/>
/// </summary>
public static class PipelineExtensions
{
    /// <summary>Adds a <see cref="MetricsInterceptor"/> to the pipeline.</summary>
    /// <param name="pipeline">The pipeline being configured.</param>
    /// <param name="eventSource">The invocation event source used to publish the metrics events.</param>
    /// <returns>The pipeline being configured.</returns>
    public static Pipeline UseMetrics(this Pipeline pipeline, InvocationEventSource eventSource) =>
        pipeline.Use(next => new MetricsInterceptor(next, eventSource));
}
