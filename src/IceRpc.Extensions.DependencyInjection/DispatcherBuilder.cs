// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Slice;

namespace IceRpc.Extensions.DependencyInjection;

/// <summary>A builder for configuring IceRpc server dispatcher.</summary>
public class DispatcherBuilder
{
    /// <summary>The service provider used by the builder.</summary>
    public IServiceProvider ApplicationServices { get; }

    private readonly Router _router;

    /// <summary>Registers a route with a path. If there is an existing route at the same path, it is replaced.
    /// </summary>
    /// <param name="path">The path of this route. It must match exactly the path of the request. In particular, it
    /// must start with a <c>/</c>.</param>
    /// <param name="dispatcher">The target of this route. It is typically an <see cref="IService"/>.</param>
    /// <exception cref="FormatException">Thrown if <paramref name="path"/> is not a valid path.</exception>
    /// <seealso cref="Mount"/>
    public DispatcherBuilder Map(string path, IDispatcher dispatcher)
    {
        _router.Map(path, dispatcher);
        return this;
    }

    /// <summary>Registers a route to a service that uses the service default path as the route path. If there is
    /// an existing route at the same path, it is replaced.</summary>
    /// <typeparam name="T">The service type used to get the default path.</typeparam>
    /// <param name="service">The target service of this route.</param>
    /// <exception cref="InvalidOperationException">Thrown if <see cref="IDispatcher.DispatchAsync"/> was already
    /// called on this router.</exception>
    /// <seealso cref="Mount"/>
    public DispatcherBuilder Map<T>(IDispatcher service) where T : class => Map(typeof(T).GetDefaultPath(), service);

    /// <summary>Registers a route with a prefix. If there is an existing route at the same prefix, it is replaced.
    /// </summary>
    /// <param name="prefix">The prefix of this route. This prefix will be compared with the start of the path of
    /// the request.</param>
    /// <param name="dispatcher">The target of this route.</param>
    /// <exception cref="FormatException">Thrown if <paramref name="prefix"/> is not a valid path.</exception>
    /// <exception cref="InvalidOperationException">Thrown if <see cref="IDispatcher.DispatchAsync"/> was already
    /// called on this router.</exception>
    /// <seealso cref="Map(string, IDispatcher)"/>
    public DispatcherBuilder Mount(string prefix, IDispatcher dispatcher)
    {
        _router.Mount(prefix, dispatcher);
        return this;
    }

    /// <summary>Creates a sub-router, configures this sub-router and mounts it (with <see cref="Mount"/>) at the
    /// given <c>prefix</c>.</summary>
    /// <param name="prefix">The prefix of the route to the sub-router.</param>
    /// <param name="configure">A delegate that configures the new sub-router.</param>
    /// <returns>The new sub-router.</returns>
    /// <exception cref="FormatException">Thrown if <paramref name="prefix"/> is not a valid path.</exception>
    public DispatcherBuilder Route(string prefix, Action<Router> configure) =>
        new DispatcherBuilder(ApplicationServices, _router.Route(prefix, configure));

    /// <summary>Installs a middleware in this dispatch pipeline. A middleware must be installed before calling
    /// <see cref="IDispatcher.DispatchAsync"/>.</summary>
    /// <param name="middleware">The middleware to install.</param>
    /// <returns>This router.</returns>
    public DispatcherBuilder Use(Func<IDispatcher, IDispatcher> middleware)
    {
        _router.Use(middleware);
        return this;
    }

    internal IDispatcher Build() => _router;

    internal DispatcherBuilder(IServiceProvider provider) : this(provider, new Router())
    {
    }

    private DispatcherBuilder(IServiceProvider provider, Router router)
    {
        ApplicationServices = provider;
        _router = router;
    }
}
