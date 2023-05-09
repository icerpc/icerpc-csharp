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
    /// <example>
    /// The following code adds the telemetry middleware to the dispatch pipeline.
    /// <code source="../../docfx/examples/IceRpc.Telemetry.Examples/TelemetryMiddlewareExamples.cs" region="UseTelemetry" lang="csharp" />
    /// </example>
    /// <seealso href="https://github.com/icerpc/icerpc-csharp/tree/main/examples/Telemetry"/>
    public static Router UseTelemetry(this Router router, ActivitySource activitySource) =>
        router.Use(next => new TelemetryMiddleware(next, activitySource));
}
