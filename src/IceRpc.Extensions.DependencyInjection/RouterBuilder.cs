// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.IO.Compression;

namespace IceRpc.Extensions.DependencyInjection;

/// <summary>A builder for configuring IceRpc servers.</summary>
public class RouterBuilder
{
    /// <summary>The service collection used by this builder.</summary>
    public IServiceCollection ServiceCollection { get; }

    /// <summary>The service provider used by this builder.</summary>
    public IServiceProvider ServiceProvider { get; }

    private readonly Router _router;

    /// <summary>Constructs a router builder.</summary>
    /// <param name="serviceCollection">The service collection.</param>
    /// <param name="serviceProvider">The service provider.</param>
    public RouterBuilder(IServiceCollection serviceCollection, IServiceProvider serviceProvider)
    {
        ServiceCollection = serviceCollection;
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
        ServiceCollection.AddOptions<DeflateOptions>().Configure(
            options => options.CompressionLevel = compressionLevel);
        ServiceCollection.AddSingleton(typeof(CompressionLevel), compressionLevel);
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
        ServiceCollection.AddSingleton(_ => dispatchEventSource);
        return this;
    }

    /// <summary>Adds the logger middleware to this router.</summary>
    /// <returns>the builder.</returns>
    public RouterBuilder UseLogger()
    {
        ServiceCollection.AddOptions<LoggerOptions>().Configure(
            options => options.LoggerFactory = ServiceProvider.GetService<ILoggerFactory>());
        return this;
    }

    /// <summary>Adds the telemetry middleware to this router.</summary>
    /// <returns>the builder.</returns>
    public RouterBuilder UseTelemetry()
    {
        ServiceCollection.AddOptions<TelemetryOptions>();
        return this;
    }

    /// <summary>Adds the telemetry middleware to this router.</summary>
    /// <param name="activitySource">The activity source used by the telemetry middleware.</param>
    /// <returns>the builder.</returns>
    public RouterBuilder UseTelemetry(ActivitySource activitySource)
    {
        ServiceCollection.AddOptions<TelemetryOptions>().Configure(
            options => options.ActivitySource = activitySource);
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
