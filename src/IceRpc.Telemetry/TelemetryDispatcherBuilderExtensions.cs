// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Telemetry;
using System.Diagnostics;

namespace IceRpc.Builder;

/// <summary>This class provides extension methods to add the telemetry middleware to a <see cref="IDispatcherBuilder"/>.
/// </summary>
public static class TelemetryDispatcherBuilderExtensions
{
    /// <summary>Adds a <see cref="TelemetryMiddleware"/> to the dispatcher builder.</summary>
    /// <param name="builder">The builder being configured.</param>
    /// <returns>The builder being configured.</returns>
    public static IDispatcherBuilder UseTelemetry(this IDispatcherBuilder builder) =>
        builder.ServiceProvider.GetService(typeof(ActivitySource)) is ActivitySource activitySource ?
        builder.Use(next => new TelemetryMiddleware(next, activitySource)) :
        throw new InvalidOperationException(
            $"could not find service of type {nameof(ActivitySource)} in service container");
}
