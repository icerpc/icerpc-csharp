// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Metrics;

namespace IceRpc.Builder;

/// <summary>This class provides extension methods to add the metrics interceptor to an <see cref="IInvokerBuilder"/>.
/// </summary>
public static class MetricsInvokerBuilderExtensions
{
    /// <summary>Adds a <see cref="MetricsInterceptor"/> to the builder. This interceptor relies on the
    /// <see cref="InvocationEventSource"/> service managed by the service provider.</summary>
    /// <param name="builder">The builder being configured.</param>
    /// <returns>The builder being configured.</returns>
    public static IInvokerBuilder UseMetrics(this IInvokerBuilder builder) =>
        builder.ServiceProvider.GetService(typeof(InvocationEventSource)) is InvocationEventSource eventSource ?
        builder.Use(next => new MetricsInterceptor(next, eventSource)) :
        throw new InvalidOperationException(
            $"could not find service of type {nameof(InvocationEventSource)} in service container");
}
