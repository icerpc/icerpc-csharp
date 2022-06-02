// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Binder;

namespace IceRpc.Configure;

/// <summary>This class provides extension methods to add deflate interceptor to a <see cref="Pipeline"/>.
/// </summary>
public static class BinderPipelineExtensions
{
    /// <summary>Adds a <see cref="BinderInterceptor"/> to the pipeline.</summary>
    /// <param name="pipeline">The pipeline being configured.</param>
    /// <param name="connectionProvider">The connection provider.</param>
    /// <returns>The pipeline being configured.</returns>
    public static Pipeline UseBinder(
        this Pipeline pipeline,
        IClientConnectionProvider connectionProvider) =>
        pipeline.Use(next => new BinderInterceptor(next, new BinderOptions(), connectionProvider));

    /// <summary>Adds a <see cref="BinderInterceptor"/> to the pipeline.</summary>
    /// <param name="pipeline">The pipeline being configured.</param>
    /// <param name="options">The options to configure the <see cref="BinderInterceptor"/>.</param>
    /// <param name="connectionProvider">The connection provider.</param>
    /// <returns>The pipeline being configured.</returns>
    public static Pipeline UseBinder(
        this Pipeline pipeline,
        BinderOptions options,
        IClientConnectionProvider connectionProvider) =>
        pipeline.Use(next => new BinderInterceptor(next, options, connectionProvider));
}
