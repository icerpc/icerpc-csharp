// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Locator;
using Microsoft.Extensions.Logging.Abstractions;

namespace IceRpc.Configure;

/// <summary>This class provides extension methods to add interop interceptors to a <see cref="Pipeline"/>.
/// </summary>
public static class LocatorPipelineExtensions
{
    /// <summary>Adds a <see cref="LocatorInterceptor"/> to the pipeline, using the specified locator proxy.
    /// </summary>
    /// <param name="pipeline">The pipeline being configured.</param>
    /// <param name="locator">The locator proxy used for the resolutions.</param>
    public static Pipeline UseLocator(this Pipeline pipeline, ILocatorPrx locator) =>
        UseLocator(
            pipeline,
            new LocatorLocationResolver(
                locator,
                NullLoggerFactory.Instance,
                new LocatorOptions()));

    /// <summary>Adds a <see cref="LocatorInterceptor"/> to the pipeline.</summary>
    /// <param name="pipeline">The pipeline being configured.</param>
    /// <param name="locationResolver">The location resolver, usually created using a
    /// <see cref="LocatorLocationResolver"/>.</param>
    /// <returns>The pipeline being configured.</returns>
    public static Pipeline UseLocator(this Pipeline pipeline, ILocationResolver locationResolver) =>
        pipeline.Use(next => new LocatorInterceptor(next, locationResolver));
}
