// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;

namespace IceRpc
{
    public static partial class Middleware
    {
        /// <summary>Options class to configure <see cref="Telemetry"/> middleware.</summary>
        public sealed class TelemetryOptions
        {
            /// <summary>If set to a non null object the <see cref="ActivitySource"/> is used to start the request
            /// <see cref="Activity"/>.</summary>
            public ActivitySource? ActivitySource { get; set; }

            /// <summary>The logger factory used to create the IceRpc logger.</summary>
            public ILoggerFactory? LoggerFactory { get; set; }
        }

        /// <summary>A middleware that start an <see cref="Activity"/> per request, following OpenTelemetry
        /// conventions. The Activity is started if <see cref="Activity.Current"/> is not null.</summary>
        /// <returns>The Tracer interceptor.</returns>
        public static Func<IDispatcher, IDispatcher> Telemetry { get; } = CustomTelemetry(new());

        /// <summary>A middleware that start an <see cref="Activity"/> per request, following OpenTelemetry
        /// conventions. The Activity is started if the ActivitySource has any active listeners, if
        /// <see cref="Activity.Current"/> is not null or if IceRpc logger is enabled.</summary>
        /// <param name="options">Options to configure the tracer interceptor.</param>
        /// <returns>The CustomTracer interceptor.</returns>
        public static Func<IDispatcher, IDispatcher> CustomTelemetry(TelemetryOptions options) =>
            next => new Internal.TelemetryDispatcher(options, next);
    }
}
