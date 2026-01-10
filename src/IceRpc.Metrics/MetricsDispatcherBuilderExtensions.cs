// Copyright (c) ZeroC, Inc.

using IceRpc.Metrics;

namespace IceRpc.Extensions.DependencyInjection;

/// <summary>Provides an extension method for <see cref="IDispatcherBuilder" /> to add the metrics middleware.
/// </summary>
public static class MetricsDispatcherBuilderExtensions
{
    /// <summary>Extension methods for <see cref="IDispatcherBuilder" />.</summary>
    /// <param name="builder">The builder being configured.</param>
    extension(IDispatcherBuilder builder)
    {
        /// <summary>Adds a <see cref="MetricsMiddleware" /> to this dispatcher builder.</summary>
        /// <returns>The builder being configured.</returns>
        public IDispatcherBuilder UseMetrics() =>
            builder.Use(next => new MetricsMiddleware(next));
    }
}
