// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Diagnostics;

namespace IceRpc
{
    public static partial class Interceptors
    {
        /// <summary>Options class to configure Tracer interceptor.</summary>
        public class TracerOptions
        {
            /// <summary>If set to a non null object the ActivitySource is used to start the request Activity.
            /// </summary>
            public ActivitySource? ActivitySource { get; set; }
            
            /// <summary>The logger factory used to create the IceRpc logger.</summary>
            public ILoggerFactory? LoggerFactory { get; set; }
        }

        /// <summary>An interceptor that start an <see cref="Activity"/> per request, following OpenTelemetry
        /// conventions. The Activity is started if the ActivitySource has any active listeners,
        /// if <see cref="Activity.Current"/> is not null or if IceRpc logger is enabled.</summary>
        /// <param name="tracerOptions">Options to configure the tracer interceptor.</param>
        /// <returns></returns>
        public static Func<IInvoker, IInvoker> Tracer(TracerOptions tracerOptions)
        {
            ILogger logger = (tracerOptions.LoggerFactory ?? NullLoggerFactory.Instance).CreateLogger("IceRpc");
            
            return next => new InlineInvoker(
                async (request, cancel) =>
                {
                    Activity? activity = tracerOptions.ActivitySource?.StartActivity(
                        $"{request.Path}/{request.Operation}",
                        ActivityKind.Client);
                    if (activity == null && (logger.IsEnabled(LogLevel.Critical) || Activity.Current != null))
                    {
                        activity = new Activity($"{request.Path}/{request.Operation}");
                        activity.Start();
                    }
                    
                    if (activity != null)
                    {
                        activity.AddTag("rpc.system", "icerpc");
                        activity.AddTag("rpc.service", request.Path);
                        activity.AddTag("rpc.method", request.Operation);
                        // TODO add additional attributes
                        // https://github.com/open-telemetry/opentelemetry-specification/blob/main/specification/trace/semantic_conventions/rpc.md#common-remote-procedure-call-conventions
                    }

                    try
                    {
                        return await next.InvokeAsync(request, cancel).ConfigureAwait(false);
                    }
                    finally
                    {
                        activity?.Stop();
                    }
                });
        }
    }
}
