// Copyright (c) ZeroC, Inc.

using AuthorizationExample;

namespace IceRpc;

/// <summary>This class provides an extension method to add an <see cref="AuthenticationTokenInterceptor" />
/// to a <see cref="Pipeline" />.</summary>
internal static class PipelineExtensions
{
    /// <summary>Adds an <see cref="AuthenticationTokenInterceptor" /> to the pipeline.</summary>
    /// <param name="pipeline">The pipeline being configured.</param>
    /// <param name="token">The authentication token.</param>
    /// <returns>The pipeline being configured.</returns>
    internal static Pipeline UseAuthenticationToken(this Pipeline pipeline, byte[] token) =>
        pipeline.Use(next => new AuthenticationTokenInterceptor(next, token));
}
