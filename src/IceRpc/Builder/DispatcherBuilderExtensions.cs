// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Slice;

namespace IceRpc.Builder;

/// <summary>This class provide extension methods to add built-in middleware to a <see cref="IDispatcherBuilder" />.
/// </summary>
public static class DispatcherBuilderExtensions
{
    /// <summary>Adds a middleware that creates and inserts the <see cref="IDispatchInformationFeature" /> feature
    /// in all requests.</summary>
    /// <param name="builder">The builder being configured.</param>
    /// <returns>The builder.</returns>
    public static IDispatcherBuilder UseDispatchInformation(this IDispatcherBuilder builder) =>
        builder.Use(next => new InlineDispatcher((request, cancellationToken) =>
        {
            request.Features = request.Features.With<IDispatchInformationFeature>(
                new DispatchInformationFeature(request));
            return next.DispatchAsync(request, cancellationToken);
        }));

    /// <summary>Adds a middleware that sets a feature in all requests.</summary>
    /// <typeparam name="TFeature">The type of the feature.</typeparam>
    /// <param name="builder">The builder being configured.</param>
    /// <param name="feature">The value of the feature to set in all requests.</param>
    /// <returns>The builder.</returns>
    public static IDispatcherBuilder UseFeature<TFeature>(this IDispatcherBuilder builder, TFeature feature) =>
        builder.Use(next => new InlineDispatcher((request, cancellationToken) =>
        {
            request.Features = request.Features.With(feature);
            return next.DispatchAsync(request, cancellationToken);
        }));

    /// <summary>Registers a route to a service that uses the service default path as the route path. If there is
    /// an existing route at the same path, it is replaced.</summary>
    /// <typeparam name="TService">The type of the DI service that will handle the requests. The implementation of this
    /// service must implement <see cref="IDispatcher" />.</typeparam>
    /// <param name="builder">The builder being configured.</param>
    /// <returns>This builder.</returns>
    /// <remarks>With Slice, it is common for <typeparamref name="TService" /> to correspond to a generated
    /// I{name}Service interface. This generated interface does not extend <see cref="IDispatcher" />.</remarks>
    public static IDispatcherBuilder Map<TService>(this IDispatcherBuilder builder) where TService : notnull =>
        builder.Map<TService>(typeof(TService).GetDefaultServicePath());
}
