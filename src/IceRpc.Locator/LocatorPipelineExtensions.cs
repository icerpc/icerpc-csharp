// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Locator;

namespace IceRpc;

/// <summary>This class provides extension methods to install the locator interceptor in a <see cref="Pipeline" />.
/// </summary>
public static class LocatorPipelineExtensions
{
    /// <summary>Adds a <see cref="LocatorInterceptor" /> to the pipeline, using the specified locator proxy.
    /// </summary>
    /// <param name="pipeline">The pipeline being configured.</param>
    /// <param name="locator">The locator proxy used for the resolutions.</param>
    /// <returns>The pipeline being configured.</returns>
    public static Pipeline UseLocator(this Pipeline pipeline, ILocatorProxy locator) =>
        UseLocator(pipeline, new LocatorLocationResolver(locator, new LocatorOptions()));

    /// <summary>Adds a <see cref="LocatorInterceptor" /> to the pipeline.</summary>
    /// <param name="pipeline">The pipeline being configured.</param>
    /// <param name="locatorLocationResolver">The locator-based location resolver instance.</param>
    /// <returns>The pipeline being configured.</returns>
    public static Pipeline UseLocator(this Pipeline pipeline, LocatorLocationResolver locatorLocationResolver) =>
        pipeline.Use(next => new LocatorInterceptor(next, locatorLocationResolver));
}
