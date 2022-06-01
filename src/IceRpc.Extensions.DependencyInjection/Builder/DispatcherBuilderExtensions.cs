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
    /// <typeparam name="TMiddlewareOptions">The type of options class to configure the middleware.</typeparam>
    /// <param name="builder">This dispatcher builder.</param>
    /// <param name="optionName">The name of the option instance.</param>
    /// <returns>The dispatcher builder.</returns>
    public static IDispatcherBuilder UseMiddleware<TMiddleware, TMiddlewareOptions>(
        this IDispatcherBuilder builder,
        string optionName)
        where TMiddlewareOptions : class
    {
        TMiddlewareOptions options =
            builder.ServiceProvider.GetRequiredService<IOptionsMonitor<TMiddlewareOptions>>().Get(optionName);
        return builder.Use(next =>
            new MiddlewareAdapter<TMiddleware>(
                ActivatorUtilities.CreateInstance<TMiddleware>(builder.ServiceProvider, next, options)));
    }

    /// <summary>Installs a middleware with an options class and the default name ("") for the options instance.
    /// </summary>
    /// <typeparam name="TMiddleware">The type of the middleware to install.</typeparam>
    /// <typeparam name="TMiddlewareOptions">The type of options class to configure the middleware.</typeparam>
    /// <param name="builder">This dispatcher builder.</param>
    /// <returns>The dispatcher builder.</returns>
    public static IDispatcherBuilder UseMiddleware<TMiddleware, TMiddlewareOptions>(this IDispatcherBuilder builder)
        where TMiddlewareOptions : class =>
        builder.UseMiddleware<TMiddleware, TMiddlewareOptions>(Options.DefaultName);
}
