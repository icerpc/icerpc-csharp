// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System.IO.Compression;

namespace IceRpc.Configure
{
    /// <summary>This class provide extension methods to add built-in interceptors to a <see cref="Pipeline"/>
    /// </summary>
    public static class PipelineExtensions
    {
        /// <summary>Adds a <see cref="BinderInterceptor"/> to the pipeline.</summary>
        /// <param name="pipeline">The pipeline being configured.</param>
        /// <param name="connectionProvider">The connection provider.</param>
        /// <param name="cacheConnection">When <c>true</c> (the default), the binder stores the connection it retrieves
        /// from its connection provider in the proxy that created the request.</param>
        /// <returns>The pipeline being configured.</returns>
        public static Pipeline UseBinder(
            this Pipeline pipeline,
            IConnectionProvider connectionProvider,
            bool cacheConnection = true) =>
            pipeline.Use(next => new BinderInterceptor(next, connectionProvider, cacheConnection));

        /// <summary>Adds an interceptor that sets a feature in all requests.</summary>
        /// <paramtype name="T">The type of the feature.</paramtype>
        /// <param name="pipeline">The pipeline being configured.</param>
        /// <param name="feature">The value of the feature to set.</param>
        public static Pipeline UseFeature<T>(this Pipeline pipeline, T feature) =>
            pipeline.Use(next => new InlineInvoker((request, cancel) =>
            {
                request.Features = request.Features.With(feature);
                return next.InvokeAsync(request, cancel);
            }));

        /// <summary>Adds a <see cref="TimeoutInterceptor"/> to the pipeline.</summary>
        /// <param name="pipeline">The pipeline being configured.</param>
        /// <param name="timeout">The timeout for the invocation.</param>
        /// <returns>The pipeline being configured.</returns>
        public static Pipeline UseTimeout(this Pipeline pipeline, TimeSpan timeout) =>
            pipeline.Use(next => new TimeoutInterceptor(next, timeout));
    }
}
