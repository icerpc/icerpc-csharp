// Copyright (c) ZeroC, Inc.

using IceRpc.Metrics;

namespace IceRpc;

/// <summary>Provides an extension method for <see cref="Router" /> to add the metrics middleware.</summary>
public static class MetricsRouterExtensions
{
    /// <summary>Extension methods for <see cref="Router" />.</summary>
    /// <param name="router">The router being configured.</param>
    extension(Router router)
    {
        /// <summary>Adds a <see cref="MetricsMiddleware" /> to the router.</summary>
        /// <returns>The router being configured.</returns>
        /// <example>
        /// The following code adds the metrics middleware to the dispatch pipeline.
        /// <code source="../../docfx/examples/IceRpc.Metrics.Examples/MetricsMiddlewareExamples.cs"
        /// region="UseMetrics" lang="csharp" />
        /// </example>
        /// <seealso href="https://github.com/icerpc/icerpc-csharp/tree/main/examples/Metrics"/>
        public Router UseMetrics() =>
            router.Use(next => new MetricsMiddleware(next));
    }
}
