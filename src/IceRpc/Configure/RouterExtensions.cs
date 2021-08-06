// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Configure
{
    /// <summary>This class provide extension methods to add built-in middleware to a <see cref="Router"/>
    /// </summary>
    public static class RouterExtensions
    {
        /// <summary>Adds the <see cref="CompressorMiddleware"/> to the router.</summary>
        /// <param name="router">The router being configured.</param>
        /// <param name="options">The options to configure the <see cref="CompressorMiddleware"/>.</param>
        /// <returns>The router being configured.</returns>
        public static Router UseCompressor(this Router router, CompressorMiddleware.Options options) =>
            router.Use(next => new CompressorMiddleware(next, options));

        /// <summary>Adds the <see cref="LoggerMiddleware"/> to the router.</summary>
        /// <param name="router">The router being configured.</param>
        /// <param name="loggerFactory">The logger factory used to create the logger.</param>
        /// <returns>The router being configured.</returns>
        public static Router UseLogger(this Router router, ILoggerFactory loggerFactory) =>
            router.Use(next => new LoggerMiddleware(next, loggerFactory));

        /// <summary>Adds the <see cref="MetricsMiddleware"/> to the router.</summary>
        /// <param name="router">The router being configured.</param>
        /// <param name="eventSource">The dispatch event source used to publish the metrics events.</param>
        /// <returns>The router being configured.</returns>
        public static Router UseMetrics(this Router router, DispatchEventSource eventSource) =>
            router.Use(next => new MetricsMiddleware(next, eventSource));

        /// <summary>Adds the <see cref="ProxyInvokerMiddleware"/> to the router.</summary>
        /// <param name="router">The router being configured.</param>
        /// <param name="invoker">The invoker of the proxies read from the requests payload.</param>
        /// <returns>The router being configured.</returns>
        public static Router UseProxyInvoker(this Router router, IInvoker invoker) =>
            router.Use(next => new ProxyInvokerMiddleware(next, invoker));

        /// <summary>Adds the <see cref="TelemetryMiddleware"/> to the router.</summary>
        /// <param name="router">The router being configured.</param>
        /// <param name="options">The options to configure the <see cref="TelemetryMiddleware"/>.</param>
        /// <returns>The router being configured.</returns>
        public static Router UseTelemetry(this Router router, TelemetryMiddleware.Options options) =>
            router.Use(next => new TelemetryMiddleware(next, options));
    }
}
