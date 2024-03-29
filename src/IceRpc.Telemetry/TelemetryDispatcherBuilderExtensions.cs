// Copyright (c) ZeroC, Inc.

using IceRpc.Telemetry;
using System.Diagnostics;

namespace IceRpc.Extensions.DependencyInjection;

/// <summary>Provides an extension method for <see cref="IDispatcherBuilder" /> to add the telemetry middleware.
/// </summary>
public static class TelemetryDispatcherBuilderExtensions
{
    /// <summary>Adds a <see cref="TelemetryMiddleware" /> to the dispatcher builder.</summary>
    /// <param name="builder">The builder being configured.</param>
    /// <returns>The builder being configured.</returns>
    public static IDispatcherBuilder UseTelemetry(this IDispatcherBuilder builder) =>
        builder.ServiceProvider.GetService(typeof(ActivitySource)) is ActivitySource activitySource ?
        builder.Use(next => new TelemetryMiddleware(next, activitySource)) :
        throw new InvalidOperationException(
            $"Could not find service of type '{nameof(ActivitySource)}' in the service container.");
}
