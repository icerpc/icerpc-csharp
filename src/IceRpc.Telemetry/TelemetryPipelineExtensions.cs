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
    public static Pipeline UseTelemetry(this Pipeline pipeline, ActivitySource activitySource) =>
        pipeline.Use(next => new TelemetryInterceptor(next, activitySource));
}
