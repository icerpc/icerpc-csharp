// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;

namespace IceRpc
{
    public static partial class Interceptors
    {
        /// <summary>Options class to configure <see cref="CustomTelemetry"/> interceptor.</summary>
        public class TelemetryOptions
        {
            /// <summary>If set to a non null object the <see cref="ActivitySource"/> is used to start the request
            /// <see cref="Activity"/>.</summary>
            public ActivitySource? ActivitySource { get; set; }

            /// <summary>The logger factory used to create the IceRpc logger.</summary>
            public ILoggerFactory? LoggerFactory { get; set; }
        }

        /// <summary>An interceptor that start an <see cref="Activity"/> per request, following OpenTelemetry
        /// conventions. The Activity is started if <see cref="Activity.Current"/> is not null.</summary>
        public static Func<IInvoker, IInvoker> Telemetry { get; } = CustomTelemetry(new());

        /// <summary>An interceptor that start an <see cref="Activity"/> per request, following OpenTelemetry
        /// conventions. The Activity is started if the ActivitySource has any active listeners,
        /// if <see cref="Activity.Current"/> is not null or if IceRpc logger is enabled.</summary>
        /// <param name="options">Options to configure the tracer interceptor.</param>
        /// <returns>The CustomTracer interceptor.</returns>
        public static Func<IInvoker, IInvoker> CustomTelemetry(TelemetryOptions options) =>
            next => new Internal.TelemetryInvoker(options, next);
    }
}
