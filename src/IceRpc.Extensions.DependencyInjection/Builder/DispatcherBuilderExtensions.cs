// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Extensions.DependencyInjection.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IceRpc.Extensions.DependencyInjection.Builder;

/// <summary>Extension methods for <see cref="IDispatcherBuilder"/>.</summary>
public static class IceRpcServiceCollectionExtensions
{
    /// <summary>Registers a standard middleware.</summary>
    /// <typeparam name="TMiddleware">The type of the middleware to register.</typeparam>
    /// <param name="builder">This dispatcher builder.</param>
    /// <returns>The dispatcher builder.</returns>
    public static IDispatcherBuilder Use<TMiddleware>(this IDispatcherBuilder builder)
        where TMiddleware : IDispatcher =>
        builder.Use(next => ActivatorUtilities.CreateInstance<TMiddleware>(builder.ServiceProvider, next));

    /// <summary>Registers a standard middleware.</summary>
    /// <typeparam name="TMiddleware">The type of the middleware to register.</typeparam>
    /// <typeparam name="TMiddlewareOptions">The type of options class to configure the middleware.</typeparam>
    /// <param name="builder">This dispatcher builder.</param>
    /// <returns>The dispatcher builder.</returns>
    // TODO: add support for named options
    public static IDispatcherBuilder Use<TMiddleware, TMiddlewareOptions>(this IDispatcherBuilder builder)
        where TMiddleware : IDispatcher
        where TMiddlewareOptions : class
    {
        TMiddlewareOptions options = builder.ServiceProvider.GetRequiredService<IOptions<TMiddlewareOptions>>().Value;
        return builder.Use(next =>
            ActivatorUtilities.CreateInstance<TMiddleware>(builder.ServiceProvider, next, options));
    }

    /// <summary>Registers a middleware with one service dependency in its DispatchAsync.</summary>
    /// <typeparam name="TMiddleware">The type of the middleware to register.</typeparam>
    /// <typeparam name="TDep">The type of the service dependency.</typeparam>
    /// <param name="builder">This dispatcher builder.</param>
    /// <returns>The dispatcher builder.</returns>
    public static IDispatcherBuilder UseMiddleware<TMiddleware, TDep>(this IDispatcherBuilder builder)
        where TMiddleware : IMiddleware<TDep>
        where TDep : notnull =>
        builder.Use(next => new MiddlewareAdapter<TDep>(
            ActivatorUtilities.CreateInstance<TMiddleware>(builder.ServiceProvider, next)));

    /// <summary>Registers a middleware with 2 service dependencies in its DispatchAsync.</summary>
    /// <typeparam name="TMiddleware">The type of the middleware to register.</typeparam>
    /// <typeparam name="TDep1">The type of the first service dependency.</typeparam>
    /// <typeparam name="TDep2">The type of the second service dependency.</typeparam>
    /// <param name="builder">This dispatcher builder.</param>
    /// <returns>The dispatcher builder.</returns>
    public static IDispatcherBuilder UseMiddleware<TMiddleware, TDep1, TDep2>(this IDispatcherBuilder builder)
        where TMiddleware : IMiddleware<TDep1, TDep2>
        where TDep1 : notnull
        where TDep2 : notnull =>
        builder.Use(next => new MiddlewareAdapter<TDep1, TDep2>(
            ActivatorUtilities.CreateInstance<TMiddleware>(builder.ServiceProvider, next)));

    /// <summary>Registers a middleware with 3 service dependencies in its DispatchAsync.</summary>
    /// <typeparam name="TMiddleware">The type of the middleware to register.</typeparam>
    /// <typeparam name="TDep1">The type of the first service dependency.</typeparam>
    /// <typeparam name="TDep2">The type of the second service dependency.</typeparam>
    /// <typeparam name="TDep3">The type of the third service dependency.</typeparam>
    /// <param name="builder">This dispatcher builder.</param>
    /// <returns>The dispatcher builder.</returns>
    public static IDispatcherBuilder UseMiddleware<TMiddleware, TDep1, TDep2, TDep3>(this IDispatcherBuilder builder)
        where TMiddleware : IMiddleware<TDep1, TDep2, TDep3>
        where TDep1 : notnull
        where TDep2 : notnull
        where TDep3 : notnull =>
        builder.Use(next => new MiddlewareAdapter<TDep1, TDep2, TDep3>(
            ActivatorUtilities.CreateInstance<TMiddleware>(builder.ServiceProvider, next)));
}
