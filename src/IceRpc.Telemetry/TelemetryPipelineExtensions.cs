// Copyright (c) ZeroC, Inc.

using IceRpc.Telemetry;
using System.Diagnostics;

namespace IceRpc;

/// <summary>This class provide extension methods to add the telemetry interceptor to a <see cref="Pipeline" />.
/// </summary>
public static class TelemetryPipelineExtensions
{
    /// <summary>Adds the <see cref="TelemetryInterceptor" /> to the pipeline.</summary>
    /// <param name="pipeline">The pipeline being configured.</param>
    /// <param name="activitySource">The <see cref="ActivitySource" /> used to start the request activity.</param>
    /// <returns>The pipeline being configured.</returns>
    /// <example>
    /// The following code adds the telemetry interceptor to the invocation pipeline.
    /// <code source="../../docfx/examples/IceRpc.Telemetry.Examples/TelemetryInterceptorExamples.cs" region="UseTelemetry" lang="csharp" />
    /// </example>
    /// <seealso href="https://github.com/icerpc/icerpc-csharp/tree/main/examples/Telemetry"/>
    public static Pipeline UseTelemetry(this Pipeline pipeline, ActivitySource activitySource) =>
        pipeline.Use(next => new TelemetryInterceptor(next, activitySource));
}
