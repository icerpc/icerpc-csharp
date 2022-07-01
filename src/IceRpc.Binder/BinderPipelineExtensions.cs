// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Binder;

namespace IceRpc;

/// <summary>This class provides extension methods to add the binder interceptor to a <see cref="Pipeline"/>.
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
        pipeline.Use(next => new BinderInterceptor(next, connectionProvider));
}
