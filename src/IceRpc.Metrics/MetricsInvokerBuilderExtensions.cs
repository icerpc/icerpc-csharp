// Copyright (c) ZeroC, Inc.

using IceRpc.Metrics;

namespace IceRpc.Extensions.DependencyInjection;

/// <summary>Provides an extension method for <see cref="IInvokerBuilder" /> to add a metrics interceptor.</summary>
public static class MetricsInvokerBuilderExtensions
{
    /// <summary>Extension methods for <see cref="IInvokerBuilder" />.</summary>
    /// <param name="builder">The builder being configured.</param>
    extension(IInvokerBuilder builder)
    {
        /// <summary>Adds a <see cref="MetricsInterceptor" /> to the builder.</summary>
        /// <returns>The builder being configured.</returns>
        public IInvokerBuilder UseMetrics() =>
            builder.Use(next => new MetricsInterceptor(next));
    }
}
