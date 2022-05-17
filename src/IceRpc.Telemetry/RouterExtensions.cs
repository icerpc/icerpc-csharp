// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Telemetry;

namespace IceRpc.Configure;

/// <summary>This class provide extension methods to add metrics middleware to a <see cref="Router"/>
/// </summary>
public static class RouterExtensions
{
    /// <summary>Adds a <see cref="TelemetryMiddleware"/> that uses the default <see cref="TelemetryOptions"/> to
    /// the router.</summary>
    /// <param name="router">The router being configured.</param>
    /// <returns>The router being configured.</returns>
    public static Router UseTelemetry(this Router router) =>
        router.UseTelemetry(new TelemetryOptions());

    /// <summary>Adds a <see cref="TelemetryMiddleware"/> to the router.</summary>
    /// <param name="router">The router being configured.</param>
    /// <param name="options">The options to configure the <see cref="TelemetryMiddleware"/>.</param>
    /// <returns>The router being configured.</returns>
    public static Router UseTelemetry(this Router router, TelemetryOptions options) =>
        router.Use(next => new TelemetryMiddleware(next, options));
}
