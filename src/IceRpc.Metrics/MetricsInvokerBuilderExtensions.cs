// Copyright (c) ZeroC, Inc.

using IceRpc.Metrics;

namespace IceRpc.Extensions.DependencyInjection;

/// <summary>Provides an extension method for <see cref="IInvokerBuilder" /> to add a metrics interceptor.</summary>
public static class MetricsInvokerBuilderExtensions
{
    /// <summary>Adds a <see cref="MetricsInterceptor" /> to the builder.</summary>
    /// <param name="builder">The builder being configured.</param>
    /// <returns>The builder being configured.</returns>
    public static IInvokerBuilder UseMetrics(this IInvokerBuilder builder) =>
        builder.Use(next => new MetricsInterceptor(next));
}
