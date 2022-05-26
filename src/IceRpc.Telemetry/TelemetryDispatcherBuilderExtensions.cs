// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Extensions.DependencyInjection;
using IceRpc.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;

namespace IceRpc.Configure;

/// <summary>This class provide extension methods to add metrics middleware to a <see cref="Router"/>
/// </summary>
public static class TelemetryDispatcherBuilderExtensions
{
    /// <summary>Adds a <see cref="TelemetryMiddleware"/> to the dispatcher being configured.</summary>
    /// <param name="builder">The dispatcher builder.</param>
    /// <returns>The dispatcher builder.</returns>
    public static DispatcherBuilder UseTelemetry(this DispatcherBuilder builder) =>
        builder.Use(
            next => new TelemetryMiddleware(
                next,
                builder.ApplicationServices.GetRequiredService<ActivitySource>(),
                builder.ApplicationServices.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance));
}
