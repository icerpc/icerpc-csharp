// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Extensions.DependencyInjection.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IceRpc.Extensions.DependencyInjection.Builder;

/// <summary>Extension methods for <see cref="IDispatcherBuilder"/>.</summary>
public static class IceRpcServiceCollectionExtensions
{
    /// <summary>Installs a middleware.</summary>
    /// <typeparam name="TMiddleware">The type of the middleware to install.</typeparam>
    /// <param name="builder">This dispatcher builder.</param>
    /// <returns>The dispatcher builder.</returns>
    public static IDispatcherBuilder UseMiddleware<TMiddleware>(this IDispatcherBuilder builder) =>
        builder.Use(next =>
            new MiddlewareAdapter<TMiddleware>(
                ActivatorUtilities.CreateInstance<TMiddleware>(builder.ServiceProvider, next)));

    /// <summary>Installs a middleware with an options class.</summary>
    /// <typeparam name="TMiddleware">The type of the middleware to install.</typeparam>
    /// <typeparam name="TMiddlewareOptions">The type of the options class.</typeparam>
    /// <param name="builder">This dispatcher builder.</param>
    /// <param name="optionsName">The name of the <typeparamref name="TMiddlewareOptions"/> instance.</param>
    /// <returns>The dispatcher builder.</returns>
    public static IDispatcherBuilder UseMiddleware<TMiddleware, TMiddlewareOptions>(
        this IDispatcherBuilder builder,
        string optionsName)
        where TMiddlewareOptions : class
    {
        TMiddlewareOptions options =
            builder.ServiceProvider.GetRequiredService<IOptionsMonitor<TMiddlewareOptions>>().Get(optionsName);
        return builder.Use(next =>
            new MiddlewareAdapter<TMiddleware>(
                ActivatorUtilities.CreateInstance<TMiddleware>(builder.ServiceProvider, next, options)));
    }

    /// <summary>Installs a middleware with an options class and the default name ("") for the options instance.
    /// </summary>
    /// <typeparam name="TMiddleware">The type of the middleware to install.</typeparam>
    /// <typeparam name="TMiddlewareOptions">The type of the options class.</typeparam>
    /// <param name="builder">This dispatcher builder.</param>
    /// <returns>The dispatcher builder.</returns>
    public static IDispatcherBuilder UseMiddleware<TMiddleware, TMiddlewareOptions>(this IDispatcherBuilder builder)
        where TMiddlewareOptions : class =>
        builder.UseMiddleware<TMiddleware, TMiddlewareOptions>(Options.DefaultName);
}
