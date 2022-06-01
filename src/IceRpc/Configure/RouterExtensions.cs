// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;

namespace IceRpc.Configure
{
    /// <summary>This class provide extension methods to add built-in middleware to a <see cref="Router"/>.
    /// </summary>
    public static class RouterExtensions
    {
        /// <summary>Adds a middleware that creates and inserts the <see cref="IDispatchInformationFeature"/> feature
        /// in all requests.</summary>
        /// <param name="router">The router being configured.</param>
        public static Router UseDispatchInformation(this Router router) =>
            router.Use(next => new InlineDispatcher((request, cancel) =>
            {
                request.Features = request.Features.With<IDispatchInformationFeature>(
                    new DispatchInformationFeature(request));
                return next.DispatchAsync(request, cancel);
            }));

        /// <summary>Adds a middleware that sets a feature in all requests.</summary>
        /// <typeparam name="TFeature">The type of the feature.</typeparam>
        /// <param name="router">The router being configured.</param>
        /// <param name="feature">The value of the feature to set in all requests.</param>
        public static Router UseFeature<TFeature>(this Router router, TFeature feature) =>
            router.Use(next => new InlineDispatcher((request, cancel) =>
            {
                request.Features = request.Features.With(feature);
                return next.DispatchAsync(request, cancel);
            }));
    }
}
