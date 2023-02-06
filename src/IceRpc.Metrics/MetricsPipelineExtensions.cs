// Copyright (c) ZeroC, Inc.

using IceRpc.Metrics;

namespace IceRpc;

/// <summary>This class provides extension methods to add the metrics interceptor to a <see cref="Pipeline" />.
/// </summary>
public static class MetricsPipelineExtensions
{
    /// <summary>Adds a <see cref="MetricsInterceptor" /> to the pipeline.</summary>
    /// <param name="pipeline">The pipeline being configured.</param>
    /// <returns>The pipeline being configured.</returns>
    public static Pipeline UseMetrics(this Pipeline pipeline) =>
        pipeline.Use(next => new MetricsInterceptor(next));
}
