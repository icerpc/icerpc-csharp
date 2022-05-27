// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IceRpc.Extensions.DependencyInjection.Builder;

/// <summary>Extension methods for <see cref="IDispatcherBuilder"/>.</summary>
public static class IceRpcServiceCollectionExtensions
{
    /// <summary>Registers a middleware.</summary>
    /// <typeparam name="TMiddleware">The type of the midlleware to register.</typeparam>
    /// <returns>The builder.</returns>
    public static IDispatcherBuilder UseMiddleware<TMiddleware>(this IDispatcherBuilder builder)
        where TMiddleware : class =>
        builder.Use(next =>
            (IDispatcher)ActivatorUtilities.CreateInstance<TMiddleware>(
                builder.ServiceProvider,
                new object[] { next }));

    /// <summary>Registers a middleware.</summary>
    /// <param name="builder">The dispatcher builder.</param>
    /// <typeparam name="TMiddleware">The type of the midlleware to register.</typeparam>
    /// <typeparam name="TMiddlewareOptions">The type of options class to configure the middleware.</typeparam>
    /// <returns>The builder.</returns>
    public static IDispatcherBuilder UseMiddleware<TMiddleware, TMiddlewareOptions>(this IDispatcherBuilder builder)
        where TMiddleware : class
        where TMiddlewareOptions : class
    {
        TMiddlewareOptions options = builder.ServiceProvider.GetRequiredService<IOptions<TMiddlewareOptions>>().Value;
        return builder.Use(next =>
            (IDispatcher)ActivatorUtilities.CreateInstance<TMiddleware>(
                builder.ServiceProvider,
                new object[] { next, options }));
    }
}
