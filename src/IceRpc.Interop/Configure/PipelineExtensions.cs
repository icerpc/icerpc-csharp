// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Interop;

namespace IceRpc.Configure
{
    /// <summary>This class provides extension methods to add interop interceptors to a <see cref="Pipeline"/>.
    /// </summary>
    public static class PipelineExtensions
    {
        /// <summary>Adds a <see cref="LocatorInterceptor"/> that use the default options to the pipeline.</summary>
        /// <param name="pipeline">The pipeline being configured.</param>
        /// <param name="locator">The locator proxy used for the resolutions.</param>
        public static Pipeline UseLocator(this Pipeline pipeline, ILocatorPrx locator) =>
            UseLocator(pipeline, locator, new LocatorOptions());

        /// <summary>Adds a <see cref="LocatorInterceptor"/> to the pipeline.</summary>
        /// <param name="pipeline">The pipeline being configured.</param>
        /// <param name="locator">The locator proxy used for the resolutions.</param>
        /// <param name="options">The options to configure the <see cref="LocatorInterceptor"/>.</param>
        /// <returns>The pipeline being configured.</returns>
        public static Pipeline UseLocator(this Pipeline pipeline, ILocatorPrx locator, LocatorOptions options)
        {
            // This location resolver can be shared by multiple location interceptor/pipelines, in particular
            // sub-pipelines created with Pipeline.With.
            var locationResolver = ILocationResolver.FromLocator(locator, options);
            return pipeline.Use(next => new LocatorInterceptor(next, locationResolver));
        }
    }
}
