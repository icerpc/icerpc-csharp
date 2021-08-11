﻿// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Configure
{
    /// <summary>This class provide extension methods to add built-in interceptors to a <see cref="Pipeline"/>
    /// </summary>
    public static class PipelineExtensions
    {
        /// <summary>Adds a <see cref="BinderInterceptor"/> to the pipeline.</summary>
        /// <param name="pipeline">The pipeline being configured.</param>
        /// <param name="connectionProvider">The connection provider.</param>
        /// <param name="cacheConnection">When <c>true</c> (the default), the binder stores the connection it retrieves
        /// from its connection provider in the proxy that created the request.</param>
        /// <returns>The pipeline being configured.</returns>
        public static Pipeline UseBinder(
            this Pipeline pipeline,
            IConnectionProvider connectionProvider,
            bool cacheConnection = true) =>
            pipeline.Use(next => new BinderInterceptor(next, connectionProvider, cacheConnection));

        /// <summary>Adds a <see cref="CompressorInterceptor"/> that uses the default <see cref="CompressOptions"/> to
        /// the pipeline.</summary>
        /// <param name="pipeline">The pipeline being configured.</param>
        public static Pipeline UseCompressor(this Pipeline pipeline) =>
            pipeline.UseCompressor(new CompressOptions());

        /// <summary>Adds a <see cref="CompressorInterceptor"/> to the pipeline.</summary>
        /// <param name="pipeline">The pipeline being configured.</param>
        /// <param name="options">The options to configure the <see cref="CompressorInterceptor"/>.</param>
        /// <returns>The pipeline being configured.</returns>
        public static Pipeline UseCompressor(this Pipeline pipeline, CompressOptions options) =>
            pipeline.Use(next => new CompressorInterceptor(next, options));

        /// <summary>Adds a <see cref="DefaultTimeoutInterceptor"/> to the pipeline.</summary>
        /// <param name="pipeline">The pipeline being configured.</param>
        /// <param name="timeout">The timeout for the invocation.</param>
        /// <returns>The pipeline being configured.</returns>
        public static Pipeline UseDefaultTimeout(this Pipeline pipeline, TimeSpan timeout) =>
            pipeline.Use(next => new DefaultTimeoutInterceptor(next, timeout));

        /// <summary>Adds a <see cref="LoggerInterceptor"/> to the pipeline.</summary>
        /// <param name="pipeline">The pipeline being configured.</param>
        /// <param name="loggerFactory">The logger factory used to create the logger.</param>
        /// <returns>The pipeline being configured.</returns>
        public static Pipeline UseLogger(this Pipeline pipeline, ILoggerFactory loggerFactory) =>
            pipeline.Use(next => new LoggerInterceptor(next, loggerFactory));

        /// <summary>Adds a <see cref="MetricsInterceptor"/> to the pipeline.</summary>
        /// <param name="pipeline">The pipeline being configured.</param>
        /// <param name="eventSource">The invocation event source used to publish the metrics events.</param>
        /// <returns>The pipeline being configured.</returns>
        public static Pipeline UseMetrics(this Pipeline pipeline, InvocationEventSource eventSource) =>
            pipeline.Use(next => new MetricsInterceptor(next, eventSource));

        /// <summary>Adds a <see cref="RetryInterceptor"/> that use the default <see cref="RetryOptions"/> to the
        /// pipeline.</summary>
        /// <param name="pipeline">The pipeline being configured.</param>
        /// <returns>The pipeline being configured.</returns>
        public static Pipeline UseRetry(this Pipeline pipeline) =>
            pipeline.UseRetry(new RetryOptions());

        /// <summary>Adds a <see cref="RetryInterceptor"/> to the pipeline.</summary>
        /// <param name="pipeline">The pipeline being configured.</param>
        /// <param name="options">The options to configure the <see cref="RetryInterceptor"/>.</param>
        /// <returns>The pipeline being configured.</returns>
        public static Pipeline UseRetry(this Pipeline pipeline, RetryOptions options) =>
            pipeline.Use(next => new RetryInterceptor(next, options));

        /// <summary>Adds the <see cref="TelemetryInterceptor"/> to the pipeline.</summary>
        /// <param name="pipeline">The pipeline being configured.</param>
        /// <returns>The pipeline being configured.</returns>
        public static Pipeline UseTelemetry(this Pipeline pipeline) =>
            pipeline.UseTelemetry(new TelemetryOptions());

        /// <summary>Adds the <see cref="TelemetryInterceptor"/> to the pipeline.</summary>
        /// <param name="pipeline">The pipeline being configured.</param>
        /// <param name="options">The options to configure the <see cref="TelemetryInterceptor"/>.</param>
        /// <returns>The pipeline being configured.</returns>
        public static Pipeline UseTelemetry(this Pipeline pipeline, TelemetryOptions options) =>
            pipeline.Use(next => new TelemetryInterceptor(next, options));
    }
}
