// Copyright (c) ZeroC, Inc.

using IceRpc.Telemetry;
using System.Diagnostics;

namespace IceRpc.Extensions.DependencyInjection;

/// <summary>Provides an extension method for <see cref="IInvokerBuilder" /> to add the telemetry interceptor.</summary>
public static class TelemetryInvokerBuilderExtensions
{
    /// <summary>Adds the <see cref="TelemetryInterceptor" /> to the builder. This interceptor relies on the
    /// <see cref="ActivitySource" /> service managed by the service provider.</summary>
    /// <param name="builder">The builder being configured.</param>
    /// <returns>The builder being configured.</returns>
    public static IInvokerBuilder UseTelemetry(this IInvokerBuilder builder) =>
        builder.ServiceProvider.GetService(typeof(ActivitySource)) is ActivitySource activitySource ?
        builder.Use(next => new TelemetryInterceptor(next, activitySource)) :
        throw new InvalidOperationException(
            $"Could not find service of type '{nameof(ActivitySource)}' in the service container.");
}
