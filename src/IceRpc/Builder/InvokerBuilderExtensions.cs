// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;

namespace IceRpc.Builder;

/// <summary>This class provide extension methods to add built-in interceptors to an <see cref="IInvokerBuilder"/>.
/// </summary>
public static class InvokerBuilderExtensions
{
    /// <summary>Adds an interceptor that sets a feature in all requests.</summary>
    /// <typeparam name="TFeature">The type of the feature.</typeparam>
    /// <param name="builder">The builder being configured.</param>
    /// <param name="feature">The value of the feature to set.</param>
    /// <returns>The builder.</returns>
    public static IInvokerBuilder UseFeature<TFeature>(this IInvokerBuilder builder, TFeature feature) =>
        builder.Use(next => new InlineInvoker((request, cancel) =>
        {
            request.Features = request.Features.With(feature);
            return next.InvokeAsync(request, cancel);
        }));
}
