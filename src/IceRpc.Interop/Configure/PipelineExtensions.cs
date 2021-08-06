// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Interop;

namespace IceRpc.Configure
{
    /// <summary>This class provide extension methods to add built-in interceptors to a <see cref="Pipeline"/>
    /// </summary>
    public static class PipelineExtensions
    {
        /// <summary>Adds the <see cref="LocatorInterceptor"/> to the pipeline.</summary>
        /// <param name="pipeline">The pipeline being configured.</param>
        /// <param name="locator">The locator proxy used for the resolutions.</param>
        /// <param name="options">The options to configure the <see cref="LocatorInterceptor"/>.</param>
        /// <returns>The pipeline being configured.</returns>
        public static Pipeline UseLocator(this Pipeline pipeline, ILocatorPrx locator, LocatorOptions options) =>
            pipeline.Use(next => new LocatorInterceptor(next, locator, options));
    }
}
