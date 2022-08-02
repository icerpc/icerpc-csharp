// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Metrics;
using IceRpc.Metrics.Internal;

namespace IceRpc.Builder;

/// <summary>This class provides extension methods to add the metrics middleware to a <see cref="IDispatcherBuilder"/>.
/// </summary>
public static class MetricsDispatcherBuilderExtensions
{
    /// <summary>Adds a <see cref="MetricsMiddleware"/> to this dispatcher builder.</summary>
    /// <param name="builder">The builder being configured.</param>
    /// <returns>The builder being configured.</returns>
    public static IDispatcherBuilder UseMetrics(this IDispatcherBuilder builder) =>
        builder.Use(next => new MetricsMiddleware(next, DispatchEventSource.Log));
}
