// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace IceRpc
{
    public static partial class Middleware
    {
        /// <summary>Options class to configure Tracer middleware.</summary>
        public class TracerOptions
        {
            /// <summary>If set to a non null object the ActivitySource is used to start the request Activity.
            /// </summary>
            public ActivitySource? ActivitySource { get; set; }

            /// <summary>The logger factory used to create the IceRpc logger.</summary>
            public ILoggerFactory? LoggerFactory { get; set; }
        }

        /// <summary>Creates a middleware that starts a dispatch <see cref="Activity"/> per each request.</summary>
        /// <returns>The new tracer middleware.</returns>
        public static Func<IDispatcher, IDispatcher> Tracer(TracerOptions tracerOptions)
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
