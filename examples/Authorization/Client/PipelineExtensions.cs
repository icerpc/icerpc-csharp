// Copyright (c) ZeroC, Inc.

using AuthorizationExample;

namespace IceRpc;

/// <summary>This class provides an extension method to add a <see cref="SessionInterceptor" />
/// to a <see cref="Pipeline" />.</summary>
internal static class PipelineExtensions
{
    /// <summary>Adds a <see cref="SessionInterceptor" /> to the pipeline.</summary>
    /// <param name="pipeline">The pipeline being configured.</param>
    /// <param name="token">The session token.</param>
    /// <returns>The pipeline being configured.</returns>
    internal static Pipeline UseSession(this Pipeline pipeline, Guid token) =>
        pipeline.Use(next => new SessionInterceptor(next, token));
}
