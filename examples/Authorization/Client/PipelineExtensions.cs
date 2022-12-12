// Copyright (c) ZeroC, Inc. All rights reserved.

using AuthorizationExample;

namespace IceRpc;

/// <summary>This class provides an extension method to add a <see cref="SessionInterceptor" />
/// to a <see cref="Pipeline" />.</summary>
public static class PipelineExtensions
{
    /// <summary>Adds a <see cref="SessionInterceptor" /> to the pipeline.</summary>
    /// <param name="pipeline">The pipeline being configured.</param>
    /// <param name="token">The session token.</param>
    /// <returns>The pipeline being configured.</returns>
    public static Pipeline UseSession(this Pipeline pipeline, ReadOnlyMemory<byte> token) =>
        pipeline.Use(next => new SessionInterceptor(next, token));
}
