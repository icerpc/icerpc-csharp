// Copyright (c) ZeroC, Inc.

using IceRpc.RequestContext;

namespace IceRpc;

/// <summary>This class provides extension methods to add the request context interceptor to a <see cref="Pipeline" />.
/// </summary>
public static class RequestContextPipelineExtensions
{
    /// <summary>Adds a <see cref="RequestContextInterceptor" /> to the pipeline.</summary>
    /// <param name="pipeline">The pipeline being configured.</param>
    /// <returns>The pipeline being configured.</returns>
    public static Pipeline UseRequestContext(this Pipeline pipeline) =>
        pipeline.Use(next => new RequestContextInterceptor(next));
}
