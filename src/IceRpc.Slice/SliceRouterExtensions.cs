// Copyright (c) ZeroC, Inc.

namespace IceRpc.Slice;

/// <summary>Provides an extension method for <see cref="Router" /> to register a route to a service that uses the
/// service default path as the route path.</summary>
public static class SliceRouterExtensions
{
    /// <summary>Registers a route to a service that uses the service default path as the route path. If there is an
    /// existing route at the same path, it is replaced.</summary>
    /// <typeparam name="TService">The service type used to get the default path.</typeparam>
    /// <param name="router">The router being configured.</param>
    /// <param name="service">The target service of this route.</param>
    /// <returns>The router being configured.</returns>
    /// <exception cref="InvalidOperationException">Thrown if <see cref="IDispatcher.DispatchAsync" /> was already
    /// called on this router.</exception>
    /// <remarks>This default path is specific to services that implement Slice interfaces and
    /// <typeparamref name="TService" /> must correspond to an I{Name}Service interface generated by the Slice compiler.
    /// </remarks>
    public static Router Map<TService>(this Router router, IDispatcher service)
        where TService : class =>
        router.Map(typeof(TService).GetDefaultServicePath(), service);
}