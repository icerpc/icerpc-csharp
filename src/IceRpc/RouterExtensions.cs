// Copyright (c) ZeroC, Inc.

using IceRpc.Features;

namespace IceRpc;

/// <summary>Provides extension methods for <see cref="Router" />.</summary>
public static class RouterExtensions
{
    /// <summary>Extension methods for <see cref="Router" />.</summary>
    /// <param name="router">The router being configured.</param>
    extension(Router router)
    {
        /// <summary>Registers a route to a service that uses the service's default path as the route path.
        /// If there is an existing route at the same path, it is replaced.</summary>
        /// <typeparam name="TService">An interface with the DefaultServicePathAttribute attribute. The path
        /// of the mapped service corresponds to the value of this attribute.</typeparam>
        /// <param name="service">The target service of this route.</param>
        /// <returns>The router being configured.</returns>
        /// <exception cref="InvalidOperationException">Thrown if <see cref="IDispatcher.DispatchAsync" />
        /// was already called on this router.</exception>
        public Router Map<TService>(IDispatcher service)
            where TService : class =>
            router.Map(typeof(TService).GetDefaultServicePath(), service);

        /// <summary>Creates a sub-router, configures this sub-router and mounts it (with
        /// <see cref="Router.Mount(string, IDispatcher)" />) at the given <c>prefix</c>.</summary>
        /// <param name="prefix">The prefix of the route to the sub-router.</param>
        /// <param name="configure">A delegate that configures the new sub-router.</param>
        /// <returns>The new sub-router.</returns>
        /// <exception cref="FormatException">Thrown if <paramref name="prefix" /> is not a valid path.
        /// </exception>
        public Router Route(string prefix, Action<Router> configure)
        {
            ServiceAddress.CheckPath(prefix);
            var subRouter = new Router($"{router.AbsolutePrefix}{prefix}");
            configure(subRouter);
            router.Mount(prefix, subRouter);
            return subRouter;
        }

        /// <summary>Adds a middleware that creates and inserts the
        /// <see cref="IDispatchInformationFeature" /> feature in all requests.</summary>
        /// <returns>The router being configured.</returns>
        public Router UseDispatchInformation() =>
            router.Use(next => new InlineDispatcher((request, cancellationToken) =>
            {
                request.Features = request.Features.With<IDispatchInformationFeature>(
                    new DispatchInformationFeature(request));
                return next.DispatchAsync(request, cancellationToken);
            }));

        /// <summary>Adds a middleware that sets a feature in all requests.</summary>
        /// <typeparam name="TFeature">The type of the feature.</typeparam>
        /// <param name="feature">The value of the feature to set in all requests.</param>
        /// <returns>The router being configured.</returns>
        public Router UseFeature<TFeature>(TFeature feature) =>
            router.Use(next => new InlineDispatcher((request, cancellationToken) =>
            {
                request.Features = request.Features.With(feature);
                return next.DispatchAsync(request, cancellationToken);
            }));
    }
}
