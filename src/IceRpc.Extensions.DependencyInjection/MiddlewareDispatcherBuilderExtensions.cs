// Copyright (c) ZeroC, Inc.

using IceRpc.Extensions.DependencyInjection.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IceRpc.Extensions.DependencyInjection;

/// <summary>Provides extension methods for <see cref="IDispatcherBuilder" /> to register middleware.</summary>
public static class MiddlewareDispatcherBuilderExtensions
{
    /// <summary>Extension methods for <see cref="IDispatcherBuilder" />.</summary>
    /// <param name="builder">This dispatcher builder.</param>
    extension(IDispatcherBuilder builder)
    {
        /// <summary>Registers a middleware with one service dependency in its DispatchAsync method. Such a middleware
        /// implements <see cref="IMiddleware{TDep}" /> and provides a single constructor that accepts a dispatcher (the
        /// next dispatcher) followed by 0 or more DI-injected services.</summary>
        /// <typeparam name="TMiddleware">The type of the middleware to register.</typeparam>
        /// <typeparam name="TDep">The type of the service dependency.</typeparam>
        /// <returns>The dispatcher builder.</returns>
        public IDispatcherBuilder UseMiddleware<TMiddleware, TDep>()
            where TMiddleware : IMiddleware<TDep>
            where TDep : notnull =>
            builder.Use(next => new MiddlewareAdapter<TDep>(
                ActivatorUtilities.CreateInstance<TMiddleware>(builder.ServiceProvider, next)));

        /// <summary>Registers a middleware with an explicit Options parameter and with one service dependency in
        /// its DispatchAsync method. Such a middleware implements <see cref="IMiddleware{TDep}" /> and provides a
        /// single constructor that accepts a dispatcher (the next dispatcher) followed by an instance of
        /// <see cref="IOptions{TMiddlewareOptions}" /> and then 0 or more DI-injected services.</summary>
        /// <typeparam name="TMiddleware">The type of the middleware to register.</typeparam>
        /// <typeparam name="TMiddlewareOptions">The type of the Options parameter of this middleware.</typeparam>
        /// <typeparam name="TDep">The type of the service dependency.</typeparam>
        /// <param name="options">The options to give to the constructor of the middleware.</param>
        /// <returns>The dispatcher builder.</returns>
        public IDispatcherBuilder UseMiddleware<TMiddleware, TMiddlewareOptions, TDep>(
            TMiddlewareOptions options)
            where TMiddleware : IMiddleware<TDep>
            where TMiddlewareOptions : class
            where TDep : notnull =>
            builder.Use(next => new MiddlewareAdapter<TDep>(
                ActivatorUtilities.CreateInstance<TMiddleware>(
                    builder.ServiceProvider,
                    next,
                    Options.Create(options))));

        /// <summary>Registers a middleware with 2 service dependencies in its DispatchAsync method. Such a
        /// middleware implements <see cref="IMiddleware{TDep1, TDep2}" /> and provides a single constructor that
        /// accepts a dispatcher (the next dispatcher) followed by 0 or more DI-injected services.</summary>
        /// <typeparam name="TMiddleware">The type of the middleware to register.</typeparam>
        /// <typeparam name="TDep1">The type of the first service dependency.</typeparam>
        /// <typeparam name="TDep2">The type of the second service dependency.</typeparam>
        /// <returns>The dispatcher builder.</returns>
        public IDispatcherBuilder UseMiddleware<TMiddleware, TDep1, TDep2>()
            where TMiddleware : IMiddleware<TDep1, TDep2>
            where TDep1 : notnull
            where TDep2 : notnull =>
            builder.Use(next => new MiddlewareAdapter<TDep1, TDep2>(
                ActivatorUtilities.CreateInstance<TMiddleware>(builder.ServiceProvider, next)));

        /// <summary>Registers a middleware with an Options parameter and with 2 service dependencies in its
        /// DispatchAsync method. Such a middleware implements <see cref="IMiddleware{TDep1, TDep2}" /> and provides
        /// a single constructor that accepts a dispatcher (the next dispatcher) followed by an instance of
        /// <see cref="IOptions{TMiddlewareOptions}" /> and then 0 or more DI-injected services.</summary>
        /// <typeparam name="TMiddleware">The type of the middleware to register.</typeparam>
        /// <typeparam name="TMiddlewareOptions">The type of Options parameter of this middleware.</typeparam>
        /// <typeparam name="TDep1">The type of the first service dependency.</typeparam>
        /// <typeparam name="TDep2">The type of the second service dependency.</typeparam>
        /// <param name="options">The options to give to the constructor of the middleware.</param>
        /// <returns>The dispatcher builder.</returns>
        public IDispatcherBuilder UseMiddleware<TMiddleware, TMiddlewareOptions, TDep1, TDep2>(
            TMiddlewareOptions options)
            where TMiddleware : IMiddleware<TDep1, TDep2>
            where TMiddlewareOptions : class
            where TDep1 : notnull
            where TDep2 : notnull =>
            builder.Use(next => new MiddlewareAdapter<TDep1, TDep2>(
                ActivatorUtilities.CreateInstance<TMiddleware>(
                    builder.ServiceProvider,
                    next,
                    Options.Create(options))));

        /// <summary>Registers a middleware with 3 service dependencies in its DispatchAsync method. Such a
        /// middleware implements <see cref="IMiddleware{TDep1, TDep2, TDep3}" /> and provides a single
        /// constructor that accepts a dispatcher (the next dispatcher) followed by 0 or more DI-injected
        /// services.</summary>
        /// <typeparam name="TMiddleware">The type of the middleware to register.</typeparam>
        /// <typeparam name="TDep1">The type of the first service dependency.</typeparam>
        /// <typeparam name="TDep2">The type of the second service dependency.</typeparam>
        /// <typeparam name="TDep3">The type of the third service dependency.</typeparam>
        /// <returns>The dispatcher builder.</returns>
        public IDispatcherBuilder UseMiddleware<TMiddleware, TDep1, TDep2, TDep3>()
            where TMiddleware : IMiddleware<TDep1, TDep2, TDep3>
            where TDep1 : notnull
            where TDep2 : notnull
            where TDep3 : notnull =>
            builder.Use(next => new MiddlewareAdapter<TDep1, TDep2, TDep3>(
                ActivatorUtilities.CreateInstance<TMiddleware>(builder.ServiceProvider, next)));

        /// <summary>Registers a middleware with an Options parameter and with 3 service dependencies in its
        /// DispatchAsync method. Such a middleware implements <see cref="IMiddleware{TDep1, TDep2, TDep3}" /> and
        /// provides a single constructor that accepts a dispatcher (the next dispatcher) followed by an instance of
        /// <see cref="IOptions{TMiddlewareOptions}" /> and then 0 or more DI-injected services.</summary>
        /// <typeparam name="TMiddleware">The type of the middleware to register.</typeparam>
        /// <typeparam name="TMiddlewareOptions">The type of Options parameter of this middleware.</typeparam>
        /// <typeparam name="TDep1">The type of the first service dependency.</typeparam>
        /// <typeparam name="TDep2">The type of the second service dependency.</typeparam>
        /// <typeparam name="TDep3">The type of the third service dependency.</typeparam>
        /// <param name="options">The options to give to the constructor of the middleware.</param>
        /// <returns>The dispatcher builder.</returns>
        public IDispatcherBuilder UseMiddleware<TMiddleware, TMiddlewareOptions, TDep1, TDep2, TDep3>(
            TMiddlewareOptions options)
            where TMiddleware : IMiddleware<TDep1, TDep2, TDep3>
            where TMiddlewareOptions : class
            where TDep1 : notnull
            where TDep2 : notnull
            where TDep3 : notnull =>
            builder.Use(next => new MiddlewareAdapter<TDep1, TDep2, TDep3>(
                ActivatorUtilities.CreateInstance<TMiddleware>(
                    builder.ServiceProvider,
                    next,
                    Options.Create(options))));
    }
}
