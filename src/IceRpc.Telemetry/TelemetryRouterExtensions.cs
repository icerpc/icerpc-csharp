// Copyright (c) ZeroC, Inc.

using IceRpc.Telemetry;
using System.Diagnostics;

namespace IceRpc;

/// <summary>This class provide extension methods to add the telemetry middleware to a <see cref="Router" />.
/// </summary>
public static class TelemetryRouterExtensions
{
    /// <summary>Adds a <see cref="TelemetryMiddleware" /> to the router.</summary>
    /// <param name="router">The router being configured.</param>
    /// <param name="activitySource">The <see cref="ActivitySource" /> is used to start the request activity.</param>
    /// <returns>The router being configured.</returns>
    public static Router UseTelemetry(this Router router, ActivitySource activitySource) =>
        router.Use(next => new TelemetryMiddleware(next, activitySource));
}
