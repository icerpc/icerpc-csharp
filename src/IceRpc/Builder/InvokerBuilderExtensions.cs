// Copyright (c) ZeroC, Inc.

using IceRpc.Features;

namespace IceRpc.Builder;

/// <summary>Provides extension methods for <see cref="IInvokerBuilder" />.</summary>
public static class InvokerBuilderExtensions
{
    /// <summary>Sets the last invoker of the invocation pipeline to be a DI service managed by the service provider.
    /// </summary>
    /// <typeparam name="TService">The type of the DI service.</typeparam>
    /// <param name="builder">The builder being configured.</param>
    /// <returns>This builder.</returns>
    public static IInvokerBuilder Into<TService>(this IInvokerBuilder builder) where TService : IInvoker
    {
        object? into = builder.ServiceProvider.GetService(typeof(TService));
        return into is not null ? builder.Into((IInvoker)into) :
            throw new InvalidOperationException(
                $"Could not find service of type '{typeof(TService)}' in the service container.");
    }

    /// <summary>Adds an interceptor that sets a feature in all requests.</summary>
    /// <typeparam name="TFeature">The type of the feature.</typeparam>
    /// <param name="builder">The builder being configured.</param>
    /// <param name="feature">The value of the feature to set.</param>
    /// <returns>The builder.</returns>
    public static IInvokerBuilder UseFeature<TFeature>(this IInvokerBuilder builder, TFeature feature) =>
        builder.Use(next => new InlineInvoker((request, cancellationToken) =>
        {
            request.Features = request.Features.With(feature);
            return next.InvokeAsync(request, cancellationToken);
        }));
}
