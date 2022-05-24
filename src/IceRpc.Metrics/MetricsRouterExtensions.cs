// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Metrics;

namespace IceRpc.Configure;

/// <summary>This class provides extension methods to add metrics middleware to a <see cref="Router"/>
/// </summary>
public static class MetricsRouterExtensions
{
    /// <summary>Adds a <see cref="MetricsMiddleware"/> to the router.</summary>
    /// <param name="router">The router being configured.</param>
    /// <param name="eventSource">The dispatch event source used to publish the metrics events.</param>
    /// <returns>The router being configured.</returns>
    public static Router UseMetrics(this Router router, DispatchEventSource eventSource) =>
        router.Use(next => new MetricsMiddleware(next, eventSource));
}
