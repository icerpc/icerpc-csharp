// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Bidir;

namespace IceRpc;

/// <summary>This class provides extension methods to add the bidir interceptor to a <see cref="Pipeline"/>.
/// </summary>
public static class BidirPipelineExtensions
{
    /// <summary>Adds a <see cref="BidirInterceptor"/> to the pipeline.</summary>
    /// <param name="pipeline">The pipeline being configured.</param>
    /// <returns>The pipeline being configured.</returns>
    public static Pipeline UseDeflate(this Pipeline pipeline) =>
        pipeline.Use(next => new BidirInterceptor(next));
}
