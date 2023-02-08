// Copyright (c) ZeroC, Inc.

using IceRpc.Locator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IceRpc;

/// <summary>This class provides extension methods to install the locator interceptor in a <see cref="Pipeline" />.
/// </summary>
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

    /// <summary>Adds a <see cref="LocatorInterceptor" /> to the pipeline.</summary>
    /// <param name="pipeline">The pipeline being configured.</param>
    /// <param name="locatorLocationResolver">The locator-based location resolver instance.</param>
    /// <returns>The pipeline being configured.</returns>
    public static Pipeline UseLocator(this Pipeline pipeline, LocatorLocationResolver locatorLocationResolver) =>
        pipeline.Use(next => new LocatorInterceptor(next, locatorLocationResolver));
}
