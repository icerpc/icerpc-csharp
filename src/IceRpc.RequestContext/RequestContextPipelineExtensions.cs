// Copyright (c) ZeroC, Inc.

using IceRpc.RequestContext;

namespace IceRpc;

/// <summary>Provides an extension method to add a request context interceptor to a <see cref="Pipeline" />.</summary>
public static class RequestContextPipelineExtensions
{
    /// <summary>Adds a <see cref="RequestContextInterceptor" /> to this pipeline.</summary>
    /// <param name="pipeline">The pipeline being configured.</param>
    /// <returns>The pipeline being configured.</returns>
    public static Pipeline UseRequestContext(this Pipeline pipeline) =>
        pipeline.Use(next => new RequestContextInterceptor(next));
}
