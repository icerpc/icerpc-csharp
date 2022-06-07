// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Telemetry;
using System.Diagnostics;

namespace IceRpc;

/// <summary>This class provide extension methods to add telemetry interceptor to a <see cref="Pipeline"/>.
/// </summary>
public static class TelemetryPipelineExtensions
{
    /// <summary>Adds the <see cref="TelemetryInterceptor"/> to the pipeline.</summary>
    /// <param name="pipeline">The pipeline being configured.</param>
    /// <param name="activitySource">If set to a non null object the <see cref="ActivitySource"/> is used to start the
    /// request and response activities.</param>
    /// <returns>The pipeline being configured.</returns>
    public static Pipeline UseTelemetry(this Pipeline pipeline, ActivitySource activitySource) =>
        pipeline.Use(next => new TelemetryInterceptor(next, activitySource));
}
