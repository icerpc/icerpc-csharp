// Copyright (c) ZeroC, Inc.

using AuthorizationExample;

namespace IceRpc;

/// <summary>This class provides an extension method to add an <see cref="AuthenticationInterceptor" /> to a <see
/// cref="Pipeline" />.</summary>
internal static class PipelineExtensions
{
    /// <summary>Adds an <see cref="AuthenticationInterceptor" /> to the pipeline.</summary>
    /// <param name="pipeline">The pipeline being configured.</param>
    /// <param name="identityToken">The encrypted identity token.</param>
    /// <returns>The pipeline being configured.</returns>
    internal static Pipeline UseAuthentication(this Pipeline pipeline, ReadOnlyMemory<byte> identityToken) =>
        pipeline.Use(next => new AuthenticationInterceptor(next, identityToken));
}
