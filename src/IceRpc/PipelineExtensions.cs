// Copyright (c) ZeroC, Inc.

using IceRpc.Features;

namespace IceRpc;

/// <summary>Provides an extension method for <see cref="Pipeline" />.</summary>
public static class PipelineExtensions
{
    /// <summary>Adds an interceptor that sets a feature in all requests.</summary>
    /// <typeparam name="TFeature">The type of the feature.</typeparam>
    /// <param name="pipeline">The pipeline being configured.</param>
    /// <param name="feature">The value of the feature to set.</param>
    /// <returns>The pipeline being configured.</returns>
    public static Pipeline UseFeature<TFeature>(this Pipeline pipeline, TFeature feature) =>
        pipeline.Use(next => new InlineInvoker((request, cancellationToken) =>
        {
            request.Features = request.Features.With(feature);
            return next.InvokeAsync(request, cancellationToken);
        }));
}
