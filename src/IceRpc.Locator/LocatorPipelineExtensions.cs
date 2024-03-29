// Copyright (c) ZeroC, Inc.

using IceRpc.Locator;
using IceRpc.Slice.Ice;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IceRpc;

/// <summary>Provides extension methods for <see cref="Pipeline" /> to add the locator interceptor.</summary>
public static class LocatorPipelineExtensions
{
    /// <summary>Adds a <see cref="LocatorInterceptor" /> to the pipeline, using the specified locator.</summary>
    /// <param name="pipeline">The pipeline being configured.</param>
    /// <param name="locator">The locator used for the resolutions.</param>
    /// <returns>The pipeline being configured.</returns>
    public static Pipeline UseLocator(this Pipeline pipeline, ILocator locator) =>
        UseLocator(pipeline, locator, NullLoggerFactory.Instance);

    /// <summary>Adds a <see cref="LocatorInterceptor" /> to the pipeline, using the specified locator.</summary>
    /// <param name="pipeline">The pipeline being configured.</param>
    /// <param name="locator">The locator used for the resolutions.</param>
    /// <param name="loggerFactory">The logger factory used to create a <see cref="ILogger{TCategoryName}" /> for
    /// <see cref="LocatorInterceptor" />.</param>
    /// <returns>The pipeline being configured.</returns>
    public static Pipeline UseLocator(this Pipeline pipeline, ILocator locator, ILoggerFactory loggerFactory) =>
        UseLocator(
            pipeline,
            new LocatorLocationResolver(
                locator,
                new LocatorOptions(),
                loggerFactory.CreateLogger<LocatorInterceptor>()));

    /// <summary>Adds a <see cref="LocatorInterceptor" /> to the pipeline, using the specified location resolver.
    /// </summary>
    /// <param name="pipeline">The pipeline being configured.</param>
    /// <param name="locationResolver">The location resolver instance.</param>
    /// <returns>The pipeline being configured.</returns>
    public static Pipeline UseLocator(this Pipeline pipeline, ILocationResolver locationResolver) =>
        pipeline.Use(next => new LocatorInterceptor(next, locationResolver));
}
