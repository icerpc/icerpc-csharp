// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Telemetry;

namespace IceRpc.Configure;

/// <summary>This class provide extension methods to add telemetry interceptor to a <see cref="Pipeline"/>.
/// </summary>
public static class TelemetryPipelineExtensions
{
    /// <summary>Adds the <see cref="TelemetryInterceptor"/> to the pipeline.</summary>
    /// <param name="pipeline">The pipeline being configured.</param>
    /// <param name="options">The options to configure the <see cref="TelemetryInterceptor"/>.</param>
    /// <returns>The pipeline being configured.</returns>
    public static Pipeline UseTelemetry(this Pipeline pipeline, TelemetryOptions options) =>
        pipeline.Use(next => new TelemetryInterceptor(next, options));
}
