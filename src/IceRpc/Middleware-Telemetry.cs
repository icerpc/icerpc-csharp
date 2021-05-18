// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Diagnostics;

namespace IceRpc
{
    public static partial class Middleware
    {
        /// <summary>Options class to configure <see cref="Telemetry"/> middleware.</summary>
        public class TelemetryOptions
        {
            /// <summary>If set to a non null object the ActivitySource is used to start the request Activity.
            /// </summary>
            public ActivitySource? ActivitySource { get; set; }

            /// <summary>The logger factory used to create the IceRpc logger.</summary>
            public ILoggerFactory? LoggerFactory { get; set; }
        }

        /// <summary>A middleware that start an <see cref="Activity"/> per request, following OpenTelemetry
        /// conventions. The Activity is started if <see cref="Activity.Current"/> is not null.</summary>
        /// <returns>The Tracer interceptor.</returns>
        public static Func<IDispatcher, IDispatcher> Telemetry { get; } = CustomTelemetry(new());

        /// <summary>A middleware that start an <see cref="Activity"/> per request, following OpenTelemetry
        /// conventions. The Activity is started if the ActivitySource has any active listeners,
        /// if <see cref="Activity.Current"/> is not null or if IceRpc logger is enabled.</summary>
        /// <param name="tracerOptions">Options to configure the tracer interceptor.</param>
        /// <returns>The CustomTracer interceptor.</returns>
        public static Func<IDispatcher, IDispatcher> CustomTelemetry(TelemetryOptions tracerOptions)
        {
            ILogger logger = (tracerOptions.LoggerFactory ?? NullLoggerFactory.Instance).CreateLogger("IceRpc");
            return next => new InlineDispatcher(
                async (request, cancel) =>
                {
                    // TODO Use CreateActivity from ActivitySource once we move to .NET 6, to avoid starting the activity
                    // before we restore its context.
                    Activity? activity = tracerOptions.ActivitySource?.StartActivity(
                        $"{request.Path}/{request.Operation}",
                        ActivityKind.Server);
                    if (activity == null && (logger.IsEnabled(LogLevel.Critical) || Activity.Current != null))
                    {
                        activity = new Activity($"{request.Path}/{request.Operation}");
                        // TODO we should start the activity after restoring its context, we should update this once
                        // we move to CreateActivity in .NET 6
                        activity.Start();
                    }

                    if (activity != null)
                    {
                        activity.AddTag("rpc.system", "icerpc");
                        activity.AddTag("rpc.service", request.Path);
                        activity.AddTag("rpc.method", request.Operation);
                        // TODO add additional attributes
                        // https://github.com/open-telemetry/opentelemetry-specification/blob/main/specification/trace/semantic_conventions/rpc.md#common-remote-procedure-call-conventions
                        request.RestoreActivityContext(activity);
                    }

                    try
                    {
                        return await next.DispatchAsync(request, cancel).ConfigureAwait(false);
                    }
                    finally
                    {
                        activity?.Stop();
                    }
                });
        }
    }
}
