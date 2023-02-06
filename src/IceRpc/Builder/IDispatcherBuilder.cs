// Copyright (c) ZeroC, Inc.

namespace IceRpc.Builder;

/// <summary>Provides the mechanism to configure a dispatcher when using Dependency Injection (DI). Each request will
/// be dispatched in its own scope.</summary>
public interface IDispatcherBuilder
{
    /// <summary>Gets the service provider.</summary>
    IServiceProvider ServiceProvider { get; }

    /// <summary>Registers a route with a path. If there is an existing route at the same path, it is replaced.
    /// </summary>
    /// <typeparam name="TService">The type of the DI service that will handle the requests. The implementation of this
    /// service must implement <see cref="IDispatcher" />.</typeparam>
    /// <param name="path">The path of this route. It must match exactly the path of the request. In particular, it
    /// must start with a <c>/</c>.</param>
    /// <exception cref="FormatException">Thrown if <paramref name="path" /> is not a valid path.</exception>
    /// <returns>This builder.</returns>
    IDispatcherBuilder Map<TService>(string path) where TService : notnull;

    /// <summary>Registers a route with a prefix. If there is an existing route at the same prefix, it is replaced.
    /// </summary>
    /// <typeparam name="TService">The type of the DI service that will handle the requests. The implementation of this
    /// service must implement <see cref="IDispatcher" />.</typeparam>
    /// <param name="prefix">The prefix of this route. This prefix will be compared with the start of the path of
    /// the request.</param>
    /// <exception cref="FormatException">Thrown if <paramref name="prefix" /> is not a valid path.</exception>
    /// <returns>This builder.</returns>
    IDispatcherBuilder Mount<TService>(string prefix) where TService : notnull;

    /// <summary>Creates a sub-router, configures this sub-router and mounts it at the given <c>prefix</c>.</summary>
    /// <param name="prefix">The prefix of the route to the sub-router.</param>
    /// <param name="configure">A delegate that configures the new sub-router.</param>
    void Route(string prefix, Action<IDispatcherBuilder> configure);

    /// <summary>Registers a middleware.</summary>
    /// <param name="middleware">The middleware to register.</param>
    /// <returns>This builder.</returns>
    IDispatcherBuilder Use(Func<IDispatcher, IDispatcher> middleware);
}
