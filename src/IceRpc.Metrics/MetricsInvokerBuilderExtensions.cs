// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Metrics;
using IceRpc.Metrics.Internal;

namespace IceRpc.Builder;

/// <summary>This class provides extension methods to add the metrics interceptor to an <see cref="IInvokerBuilder"/>.
/// </summary>
public static class MetricsInvokerBuilderExtensions
{
    /// <summary>Adds a <see cref="MetricsInterceptor"/> to the builder.</summary>
    /// <param name="builder">The builder being configured.</param>
    /// <returns>The builder being configured.</returns>
    public static IInvokerBuilder UseMetrics(this IInvokerBuilder builder) =>
        builder.Use(next => new MetricsInterceptor(next, InvocationEventSource.Log));
}
