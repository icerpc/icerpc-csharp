// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Telemetry;
using System.Diagnostics;

namespace IceRpc.Configure;

/// <summary>This class provide extension methods to add metrics middleware to a <see cref="Router"/>.
/// </summary>
public static class TelemetryRouterExtensions
{
    /// <summary>Adds a <see cref="TelemetryMiddleware"/> to the router.</summary>
    /// <param name="router">The router being configured.</param>
    /// <param name="activitySource">If set to a non null object the <see cref="ActivitySource"/> is used to start the
    /// request and response activities.</param>
    /// <returns>The router being configured.</returns>
    public static Router UseTelemetry(this Router router, ActivitySource activitySource) =>
        router.Use(next => new TelemetryMiddleware(next, activitySource));
}
