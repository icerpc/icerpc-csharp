// Copyright (c) ZeroC, Inc.

using IceRpc.RequestContext;

namespace IceRpc;

/// <summary>Provides an extension method for <see cref="Pipeline" /> to add the request context interceptor.</summary>
public static class RequestContextPipelineExtensions
{
    /// <summary>Adds a <see cref="RequestContextInterceptor" /> to this pipeline.</summary>
    /// <param name="pipeline">The pipeline being configured.</param>
    /// <returns>The pipeline being configured.</returns>
    /// <example>
    /// The following code adds the request context interceptor to the invocation pipeline.
    /// <code source="../../docfx/examples/IceRpc.RequestContext.Examples/RequestContextInterceptorExamples.cs" region="UseRequestContext" lang="csharp" />
    /// The following code shows how to set the request context feature with a Slice proxy.
    /// <code source="../../docfx/examples/IceRpc.RequestContext.Examples/RequestContextInterceptorExamples.cs" region="UseRequestContextWithSliceProxy" lang="csharp" />
    /// The following code shows how to set the request context feature with a Protobuf client.
    /// <code source="../../docfx/examples/IceRpc.RequestContext.Examples/RequestContextInterceptorExamples.cs" region="UseRequestContextWithProtobufClient" lang="csharp" />
    /// </example>
    /// <seealso href="https://github.com/icerpc/icerpc-csharp/tree/0.2.x/examples/RequestContext"/>
    public static Pipeline UseRequestContext(this Pipeline pipeline) =>
        pipeline.Use(next => new RequestContextInterceptor(next));
}
