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
    public static Router UseMetrics(this Router router) =>
        router.Use(next => new MetricsMiddleware(next));
}
