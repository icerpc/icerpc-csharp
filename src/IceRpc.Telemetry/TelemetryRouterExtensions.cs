// Copyright (c) ZeroC, Inc.

using IceRpc.Telemetry;
using System.Diagnostics;

namespace IceRpc;

/// <summary>Provides an extension method for <see cref="Router" /> to add the telemetry middleware.</summary>
public static class TelemetryRouterExtensions
{
    /// <summary>Extension methods for <see cref="Router" />.</summary>
    /// <param name="router">The router being configured.</param>
    extension(Router router)
    {
        /// <summary>Adds a <see cref="TelemetryMiddleware" /> to the router.</summary>
        /// <param name="activitySource">The <see cref="ActivitySource" /> is used to start the request
        /// activity.</param>
        /// <returns>The router being configured.</returns>
        /// <example>
        /// The following code adds the telemetry middleware to the dispatch pipeline.
        /// <code source="../../docfx/examples/IceRpc.Telemetry.Examples/TelemetryMiddlewareExamples.cs"
        /// region="UseTelemetry" lang="csharp" />
        /// </example>
        /// <seealso href="https://github.com/icerpc/icerpc-csharp/tree/main/examples/Telemetry"/>
        public Router UseTelemetry(ActivitySource activitySource) =>
            router.Use(next => new TelemetryMiddleware(next, activitySource));
    }
}
