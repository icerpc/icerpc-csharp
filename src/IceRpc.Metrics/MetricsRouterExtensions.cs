// Copyright (c) ZeroC, Inc.

using IceRpc.Metrics;

namespace IceRpc;

/// <summary>This class provides extension methods to add the metrics middleware to a <see cref="Router" />.
/// </summary>
public static class MetricsRouterExtensions
{
    /// <summary>Adds a <see cref="MetricsMiddleware" /> to the router.</summary>
    /// <param name="router">The router being configured.</param>
    /// <returns>The router being configured.</returns>
    /// <example>
    /// The following code adds the metrics middleware to the dispatch pipeline.
    /// <code source="../../docfx/examples/IceRpc.Metrics.Examples/MetricsMiddlewareExamples.cs" region="UseMetrics" lang="csharp" />
    /// </example>
    /// <seealso href="https://github.com/icerpc/icerpc-csharp/tree/main/examples/Metrics"/>
    public static Router UseMetrics(this Router router) =>
        router.Use(next => new MetricsMiddleware(next));
}
