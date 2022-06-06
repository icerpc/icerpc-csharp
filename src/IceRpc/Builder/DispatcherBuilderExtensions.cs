// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;

namespace IceRpc.Builder;

/// <summary>This class provide extension methods to add built-in middleware to a <see cref="IDispatcherBuilder"/>.
/// </summary>
public static class DispatcherBuilderExtensions
{
    /// <summary>Adds a middleware that creates and inserts the <see cref="IDispatchInformationFeature"/> feature
    /// in all requests.</summary>
    /// <param name="builder">The builder being configured.</param>
    /// <returns>The builder.</returns>
    public static IDispatcherBuilder UseDispatchInformation(this IDispatcherBuilder builder) =>
        builder.Use(next => new InlineDispatcher((request, cancel) =>
        {
            request.Features = request.Features.With<IDispatchInformationFeature>(
                new DispatchInformationFeature(request));
            return next.DispatchAsync(request, cancel);
        }));

    /// <summary>Adds a middleware that sets a feature in all requests.</summary>
    /// <typeparam name="TFeature">The type of the feature.</typeparam>
    /// <param name="builder">The builder being configured.</param>
    /// <param name="feature">The value of the feature to set in all requests.</param>
    /// <returns>The builder.</returns>
    public static IDispatcherBuilder UseFeature<TFeature>(this IDispatcherBuilder builder, TFeature feature) =>
        builder.Use(next => new InlineDispatcher((request, cancel) =>
        {
            request.Features = request.Features.With(feature);
            return next.DispatchAsync(request, cancel);
        }));
}
