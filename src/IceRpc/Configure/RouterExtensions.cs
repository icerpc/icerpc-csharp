// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace IceRpc.Configure
{
    /// <summary>This class provide extension methods to add built-in middleware to a <see cref="Router"/>
    /// </summary>
    public static class RouterExtensions
    {
        /// <summary>Adds a <see cref="CompressorMiddleware"/> that uses the default <see cref="CompressOptions"/> to
        /// the router.</summary>
        /// <param name="router">The router being configured.</param>
        /// <returns>The router being configured.</returns>
        public static Router UseCompressor(this Router router) =>
            router.UseCompressor(new CompressOptions());

        /// <summary>Adds a <see cref="CompressorMiddleware"/> to the router.</summary>
        /// <param name="router">The router being configured.</param>
        /// <param name="options">The options to configure the <see cref="CompressorMiddleware"/>.</param>
        /// <returns>The router being configured.</returns>
        public static Router UseCompressor(this Router router, CompressOptions options) =>
            router.Use(next => new CompressorMiddleware(next, options));

        /// <summary>Adds a <see cref="LoggerMiddleware"/> to the router.</summary>
        /// <param name="router">The router being configured.</param>
        /// <param name="loggerFactory">The logger factory used to create the logger.</param>
        /// <returns>The router being configured.</returns>
        public static Router UseLogger(this Router router, ILoggerFactory loggerFactory) =>
            router.Use(next => new LoggerMiddleware(next, loggerFactory));

        /// <summary>Adds a <see cref="MetricsMiddleware"/> to the router.</summary>
        /// <param name="router">The router being configured.</param>
        /// <param name="eventSource">The dispatch event source used to publish the metrics events.</param>
        /// <returns>The router being configured.</returns>
        public static Router UseMetrics(this Router router, DispatchEventSource eventSource) =>
            router.Use(next => new MetricsMiddleware(next, eventSource));

        /// <summary>Adds a middleware that sets a feature in the request's features.</summary>
        /// <paramtype name="T">The type of the feature.</paramtype>
        /// <param name="router">The router being configured.</param>
        /// <param name="feature">The value of the feature to set in all requests.</param>
        public static Router UseRequestFeature<T>(this Router router, T feature) =>
            router.Use(next => new InlineDispatcher((request, cancel) =>
            {
                request.Features = request.Features.With(feature);
                return next.DispatchAsync(request, cancel);
            }));

        /// <summary>Adds a <see cref="TelemetryMiddleware"/> that uses the default <see cref="TelemetryOptions"/> to
        /// the router.</summary>
        /// <param name="router">The router being configured.</param>
        /// <returns>The router being configured.</returns>
        public static Router UseTelemetry(this Router router) =>
            router.UseTelemetry(new TelemetryOptions());

        /// <summary>Adds a <see cref="TelemetryMiddleware"/> to the router.</summary>
        /// <param name="router">The router being configured.</param>
        /// <param name="options">The options to configure the <see cref="TelemetryMiddleware"/>.</param>
        /// <returns>The router being configured.</returns>
        public static Router UseTelemetry(this Router router, TelemetryOptions options) =>
            router.Use(next => new TelemetryMiddleware(next, options));
    }
}
