// Copyright (c) ZeroC, Inc.

using IceRpc.Metrics;

namespace IceRpc;

/// <summary>Provides an extension method for <see cref="Pipeline" /> to add the metrics interceptor.</summary>
public static class MetricsPipelineExtensions
{
    /// <summary>Extension methods for <see cref="Pipeline" />.</summary>
    /// <param name="pipeline">The pipeline being configured.</param>
    extension(Pipeline pipeline)
    {
        /// <summary>Adds a <see cref="MetricsInterceptor" /> to the pipeline.</summary>
        /// <returns>The pipeline being configured.</returns>
        /// <example>
        /// The following code adds the metrics interceptor to the invocation pipeline.
        /// <code source="../../docfx/examples/IceRpc.Metrics.Examples/MetricsInterceptorExamples.cs"
        /// region="UseMetrics" lang="csharp" />
        /// </example>
        /// <seealso href="https://github.com/icerpc/icerpc-csharp/tree/main/examples/Metrics"/>
        public Pipeline UseMetrics() =>
            pipeline.Use(next => new MetricsInterceptor(next));
    }
}
