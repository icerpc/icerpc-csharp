// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Binder;

namespace IceRpc.Configure;

/// <summary>This class provides extension methods to add deflate interceptor to a <see cref="Pipeline"/>
/// </summary>
public static class BinderPipelineExtensions
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
}
