// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Configure
{
    /// <summary>This class provides extension methods to add interop interceptors to a <see cref="Pipeline"/>.
    /// </summary>
    public static class InteropPipelineExtensions
    {
        /// <summary>Adds a <see cref="LocatorInterceptor"/> to the pipeline, using the specified locator proxy.
        /// </summary>
        /// <param name="pipeline">The pipeline being configured.</param>
        /// <param name="locator">The locator proxy used for the resolutions.</param>
        public static Pipeline UseLocator(this Pipeline pipeline, ILocatorPrx locator) =>
            UseLocator(pipeline, new LocatorOptions { Locator = locator });

        /// <summary>Adds a <see cref="LocatorInterceptor"/> to the pipeline.</summary>
        /// <param name="pipeline">The pipeline being configured.</param>
        /// <param name="options">The options to configure the <see cref="LocatorInterceptor"/>.</param>
        /// <returns>The pipeline being configured.</returns>
        public static Pipeline UseLocator(this Pipeline pipeline, LocatorOptions options)
        {
            // This location resolver can be shared by multiple location interceptor/pipelines, in particular
            // sub-pipelines created with Pipeline.With.
            var locationResolver = ILocationResolver.FromLocator(options);
            return pipeline.Use(next => new LocatorInterceptor(next, locationResolver));
        }
    }
}
