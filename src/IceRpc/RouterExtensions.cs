// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Slice;

namespace IceRpc;

/// <summary>Provides extension methods for <see cref="Router" />.</summary>
public static class RouterExtensions
{
    /// <summary>Registers a route to a service that uses the service default path as the route path. If there is
    /// an existing route at the same path, it is replaced.</summary>
    /// <typeparam name="TService">The service type used to get the default path.</typeparam>
    /// <param name="router">The router being configured.</param>
    /// <param name="service">The target service of this route.</param>
    /// <returns>The router being configured.</returns>
    /// <exception cref="InvalidOperationException">Thrown if <see cref="IDispatcher.DispatchAsync" /> was already
    /// called on this router.</exception>
    /// <seealso cref="Router.Mount(string, IDispatcher)" />
    public static Router Map<TService>(this Router router, IDispatcher service)
        where TService : class =>
        router.Map(typeof(TService).GetDefaultPath(), service);

    /// <summary>Creates a sub-router, configures this sub-router and mounts it (with
    /// <see cref="Router.Mount(string, IDispatcher)" />) at the given <c>prefix</c>.</summary>
    /// <param name="router">The router being configured.</param>
    /// <param name="prefix">The prefix of the route to the sub-router.</param>
    /// <param name="configure">A delegate that configures the new sub-router.</param>
    /// <returns>The new sub-router.</returns>
    /// <exception cref="FormatException">Thrown if <paramref name="prefix" /> is not a valid path.</exception>
    public static Router Route(this Router router, string prefix, Action<Router> configure)
    {
        ServiceAddress.CheckPath(prefix);
        var subRouter = new Router($"{router.AbsolutePrefix}{prefix}");
        configure(subRouter);
        router.Mount(prefix, subRouter);
        return subRouter;
    }

    /// <summary>Adds a middleware that creates and inserts the <see cref="IDispatchInformationFeature" /> feature
    /// in all requests.</summary>
    /// <param name="router">The router being configured.</param>
    /// <returns>The router being configured.</returns>
    public static Router UseDispatchInformation(this Router router) =>
        router.Use(next => new InlineDispatcher((request, cancellationToken) =>
        {
            request.Features = request.Features.With<IDispatchInformationFeature>(
                new DispatchInformationFeature(request));
            return next.DispatchAsync(request, cancellationToken);
        }));

    /// <summary>Adds a middleware that sets a feature in all requests.</summary>
    /// <typeparam name="TFeature">The type of the feature.</typeparam>
    /// <param name="router">The router being configured.</param>
    /// <param name="feature">The value of the feature to set in all requests.</param>
    /// <returns>The router being configured.</returns>
    public static Router UseFeature<TFeature>(this Router router, TFeature feature) =>
        router.Use(next => new InlineDispatcher((request, cancellationToken) =>
        {
            request.Features = request.Features.With(feature);
            return next.DispatchAsync(request, cancellationToken);
        }));
}
