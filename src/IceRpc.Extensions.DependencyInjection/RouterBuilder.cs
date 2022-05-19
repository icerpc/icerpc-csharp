// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Logger;
using IceRpc.Metrics;
using IceRpc.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.IO.Compression;

namespace IceRpc.Extensions.DependencyInjection;

/// <summary>A builder for configuring IceRpc servers.</summary>
public class RouterBuilder
{
    /// <summary>The service provider used by this builder.</summary>
    public IServiceProvider ServiceProvider { get; }

    private readonly Router _router;

    /// <summary>Constructs a router builder.</summary>
    /// <param name="serviceProvider">The service provider.</param>
    public RouterBuilder(IServiceProvider serviceProvider)
    {
        ServiceProvider = serviceProvider;
        _router = new Router();
    }

    /// <summary>Adds the deflate middleware with the default compression level to this router.</summary>
    /// <returns>the builder.</returns>
    public RouterBuilder UseDeflate() => UseDeflate(CompressionLevel.Fastest);

    /// <summary>Adds the deflate middleware to this router.</summary>
    /// <param name="compressionLevel">The compression level to use.</param>
    /// <returns>the builder.</returns>
    public RouterBuilder UseDeflate(CompressionLevel compressionLevel)
    {
        _router.UseDeflate(compressionLevel);
        return this;
    }

    /// <summary>Adds the metrics middleware with the default dispatch event source to this router.</summary>
    /// <returns>the builder.</returns>
    public RouterBuilder UseMetrics() => UseMetrics(DispatchEventSource.Log);

    /// <summary>Adds the metrics middleware to this router.</summary>
    /// <param name="dispatchEventSource">The dispatch event source used by the metrics middleware.</param>
    /// <returns>the builder.</returns>
    public RouterBuilder UseMetrics(DispatchEventSource dispatchEventSource)
    {
        _router.UseMetrics(dispatchEventSource);
        return this;
    }

    /// <summary>Adds the logger middleware to this router.</summary>
    /// <returns>the builder.</returns>
    public RouterBuilder UseLogger()
    {
        _router.Use(next => ActivatorUtilities.CreateInstance<LoggerMiddleware>(
            ServiceProvider, 
            new object[] { next }));
        return this;
    }

    /// <summary>Adds the telemetry middleware to this router.</summary>
    /// <returns>the builder.</returns>
    public RouterBuilder UseTelemetry()
    {
        _router.UseTelemetry();
        return this;
    }

    /// <summary>Adds the telemetry middleware to this router.</summary>
    /// <param name="activitySource">The activity source used by the telemetry middleware.</param>
    /// <returns>the builder.</returns>
    public RouterBuilder UseTelemetry(ActivitySource activitySource)
    {
        _router.Use(next => ActivatorUtilities.CreateInstance<TelemetryMiddleware>(ServiceProvider));
        return this;
    }

    /// <summary>Registers a route to a service that uses the service default path as the route path. If there is
    /// an existing route at the same path, it is replaced.</summary>
    /// <typeparam name="T">The service type used to get the default path.</typeparam>
    /// <param name="service">The target service of this route.</param>
    /// <exception cref="InvalidOperationException">Thrown if <see cref="IDispatcher.DispatchAsync"/> was already
    /// called on this router.</exception>
    public void Map<T>(IDispatcher service) where T : class => _router.Map<T>(service);

    /// <summary>Build the router.</summary>
    /// <returns>The router.</returns>
    public Router Build()
    {
        if (ServiceProvider.GetService<IOptions<DeflateOptions>>() is IOptions<DeflateOptions> deflateOptions)
        {
            _router.UseDeflate(deflateOptions.Value.CompressionLevel);
        }

        if (ServiceProvider.GetService<IOptions<LoggerOptions>>() is IOptions<LoggerOptions> loggerOptions &&
            loggerOptions.Value.LoggerFactory != null)
        {
            _router.UseLogger(loggerOptions.Value.LoggerFactory);
        }

        if (ServiceProvider.GetService<IOptions<MetricsOptions>>() is IOptions<MetricsOptions> metricsOptions &&
            metricsOptions.Value.DispatchEventSource != null)
        {
            _router.UseMetrics(metricsOptions.Value.DispatchEventSource);
        }

        if (ServiceProvider.GetService<IOptions<TelemetryOptions>>() is IOptions<TelemetryOptions> telemetryOptions)
        {
            _router.UseTelemetry(
                new Configure.TelemetryOptions
                {
                    ActivitySource = telemetryOptions.Value.ActivitySource,
                    LoggerFactory = ServiceProvider.GetService<ILoggerFactory>()
                });
        }
        return _router;
    }

#pragma warning disable CA1812
    internal class DeflateOptions
    {
        public CompressionLevel CompressionLevel { get; set; }
    }

    internal class LoggerOptions
    {
        public ILoggerFactory? LoggerFactory { get; set; }
    }

    internal class MetricsOptions
    {
        public DispatchEventSource? DispatchEventSource { get; set; }
    }

    internal class TelemetryOptions
    {
        public ActivitySource? ActivitySource { get; set; }
    }
#pragma warning restore
}
