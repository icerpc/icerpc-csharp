// Copyright (c) ZeroC, Inc.

using IceRpc.Features;

namespace IceRpc;

/// <summary>Provides an extension method for <see cref="Pipeline" /> to add an interceptor that sets a feature in all
/// requests.</summary>
public static class FeaturePipelineExtensions
{
    /// <summary>Extension methods for <see cref="Pipeline" />.</summary>
    /// <param name="pipeline">The pipeline being configured.</param>
    extension(Pipeline pipeline)
    {
        /// <summary>Adds an interceptor that sets a feature in all requests.</summary>
        /// <typeparam name="TFeature">The type of the feature.</typeparam>
        /// <param name="feature">The value of the feature to set.</param>
        /// <returns>The pipeline being configured.</returns>
        public Pipeline UseFeature<TFeature>(TFeature feature) =>
            pipeline.Use(next => new InlineInvoker((request, cancellationToken) =>
            {
                request.Features = request.Features.With(feature);
                return next.InvokeAsync(request, cancellationToken);
            }));
    }
}
