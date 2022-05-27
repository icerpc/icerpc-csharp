// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Extensions.DependencyInjection.Builder;

/// <summary>A builder for configuring IceRpc server dispatcher.</summary>
public interface IDispatcherBuilder
{
    /// <summary>The service provider used by the builder.</summary>
    public IServiceProvider ServiceProvider { get; }

    /// <summary>Registers a route with a path. If there is an existing route at the same path, it is replaced.
    /// </summary>
    /// <param name="path">The path of this route. It must match exactly the path of the request. In particular, it
    /// must start with a <c>/</c>.</param>
    /// <typeparam name="T">The dispatcher type.</typeparam>
    /// <exception cref="FormatException">Thrown if <paramref name="path"/> is not a valid path.</exception>
    /// <seealso cref="Mount"/>
    public DispatcherBuilder Map<T>(string path) where T : IDispatcher;

    /// <summary>Registers a route to a service that uses the service default path as the route path. If there is
    /// an existing route at the same path, it is replaced.</summary>
    /// <typeparam name="TService">The service type used to get the default path.</typeparam>
    /// <typeparam name="TDispatcher">The type of the target dispatcher.</typeparam>
    /// <exception cref="InvalidOperationException">Thrown if <see cref="IDispatcher.DispatchAsync"/> was already
    /// called on this router.</exception>
    /// <seealso cref="Mount"/>
    public DispatcherBuilder Map<TService, TDispatcher>()
        where TService : class
        where TDispatcher : IDispatcher;

    /// <summary>Registers a route with a prefix. If there is an existing route at the same prefix, it is replaced.
    /// </summary>
    /// <param name="prefix">The prefix of this route. This prefix will be compared with the start of the path of
    /// the request.</param>
    /// <typeparam name="T">The type of the target dispatcher.</typeparam>
    /// <exception cref="FormatException">Thrown if <paramref name="prefix"/> is not a valid path.</exception>
    public DispatcherBuilder Mount<T>(string prefix) where T : IDispatcher;

    /// <summary>Installs a middleware in this dispatch pipeline.</summary>
    /// <typeparam name="T">The type of the middleware being install.</typeparam>
    /// <returns>The builder.</returns>
    public DispatcherBuilder UseMiddleware<T>() where T : IDispatcher;

    /// <summary>Installs a middleware in this dispatch pipeline.</summary>
    /// <typeparam name="T">The type of the middleware being install.</typeparam>
    /// <typeparam name="TOptions">The type of the middleware options install.</typeparam>
    /// <returns>The builder.</returns>
    public DispatcherBuilder UseMiddleware<T, TOptions>()
        where T : IDispatcher
        where TOptions : class;
}
