// Copyright (c) ZeroC, Inc.

using IceRpc.Features;

namespace IceRpc.Extensions.DependencyInjection;

/// <summary>Provides extension methods for <see cref="IInvokerBuilder" /> to add the last invoker of an invocation
/// pipeline or to add a middleware that sets a feature.</summary>
public static class InvokerBuilderExtensions
{
    /// <summary>Extension methods for <see cref="IInvokerBuilder" />.</summary>
    /// <param name="builder">The builder being configured.</param>
    extension(IInvokerBuilder builder)
    {
        /// <summary>Sets the last invoker of the invocation pipeline to be a DI service managed by the service
        /// provider.</summary>
        /// <typeparam name="TService">The type of the DI service.</typeparam>
        /// <returns>This builder.</returns>
        public IInvokerBuilder Into<TService>() where TService : IInvoker
        {
            object? into = builder.ServiceProvider.GetService(typeof(TService));
            return into is not null ? builder.Into((IInvoker)into) :
                throw new InvalidOperationException(
                    $"Could not find service of type '{typeof(TService)}' in the service container.");
        }

        /// <summary>Adds an interceptor that sets a feature in all requests.</summary>
        /// <typeparam name="TFeature">The type of the feature.</typeparam>
        /// <param name="feature">The value of the feature to set.</param>
        /// <returns>The builder.</returns>
        public IInvokerBuilder UseFeature<TFeature>(TFeature feature) =>
            builder.Use(next => new InlineInvoker((request, cancellationToken) =>
            {
                request.Features = request.Features.With(feature);
                return next.InvokeAsync(request, cancellationToken);
            }));
    }
}
