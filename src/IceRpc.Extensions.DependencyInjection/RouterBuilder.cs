// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Deflate;
using IceRpc.Logger;
using IceRpc.Metrics;
using IceRpc.Telemetry;
using Microsoft.Extensions.DependencyInjection;
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
        _router.Use(next => ActivatorUtilities.CreateInstance<DeflateMiddleware>(
            ServiceProvider,
            new object[] { next, compressionLevel }));
        return this;
    }

    /// <summary>Adds the metrics middleware to this router.</summary>
    /// <returns>the builder.</returns>
    public RouterBuilder UseMetrics()
    {
        _router.Use(next => ActivatorUtilities.CreateInstance<MetricsMiddleware>(
            ServiceProvider,
            new object[] { next }));
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
        _router.Use(next => ActivatorUtilities.CreateInstance<TelemetryMiddleware>(
            ServiceProvider,
            new object[] { next }));
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
    public Router Build() => _router;
}
