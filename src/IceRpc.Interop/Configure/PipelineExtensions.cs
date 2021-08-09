// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Interop;

namespace IceRpc.Configure
{
    /// <summary>This class provides extension methods to add interop interceptors to a <see cref="Pipeline"/>
    /// </summary>
    public static class PipelineExtensions
    {
        /// <summary>Adds the <see cref="LocatorInterceptor"/> that use the default options to the pipeline.
        /// </summary>
        /// <param name="pipeline">The pipeline being configured.</param>
        /// <param name="locator">The locator proxy used for the resolutions.</param>
        public static Pipeline UseLocator(this Pipeline pipeline, ILocatorPrx locator) =>
            UseLocator(pipeline, locator, LocatorOptions.Default);

        /// <summary>Adds the <see cref="LocatorInterceptor"/> to the pipeline.</summary>
        /// <param name="pipeline">The pipeline being configured.</param>
        /// <param name="locator">The locator proxy used for the resolutions.</param>
        /// <param name="options">The options to configure the <see cref="LocatorInterceptor"/>.</param>
        /// <returns>The pipeline being configured.</returns>
        public static Pipeline UseLocator(this Pipeline pipeline, ILocatorPrx locator, LocatorOptions options) =>
            // TODO if the function pass to Use is called multiple times we end up with multiple locator interceptor
            // instances.
            pipeline.Use(next => new LocatorInterceptor(next, locator, options));
    }
}
