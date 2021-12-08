// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace IceRpc.Configure
{
    /// <summary>Options class to configure <see cref="TelemetryMiddleware"/> and <see cref="TelemetryInterceptor"/>.
    /// </summary>
    public sealed class TelemetryOptions
    {
        /// <summary>If set to a non null object the <see cref="ActivitySource"/> is used to start the request and
        /// response activities.</summary>
        public ActivitySource? ActivitySource { get; set; }

        /// <summary>The logger factory used to create the IceRpc logger.</summary>
        public ILoggerFactory? LoggerFactory { get; set; }
    }
}
