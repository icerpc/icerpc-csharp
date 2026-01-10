// Copyright (c) ZeroC, Inc.

using IceRpc.Extensions.DependencyInjection.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace IceRpc.Extensions.DependencyInjection;

/// <summary>Provides an extension method for <see cref="IServiceCollection" /> to add a dispatcher.</summary>
public static class DispatcherServiceCollectionExtensions
{
    /// <summary>Extension methods for <see cref="IServiceCollection" />.</summary>
    /// <param name="services">The service collection to add services to.</param>
    extension(IServiceCollection services)
    {
        /// <summary>Adds an <see cref="IDispatcher" /> to this service collection.</summary>
        /// <param name="configure">The action to configure the new dispatcher.</param>
        /// <returns>The service collection.</returns>
        public IServiceCollection AddIceRpcDispatcher(Action<IDispatcherBuilder> configure) =>
            services.AddSingleton(provider =>
            {
                var builder = new DispatcherBuilder(provider);
                configure(builder);
                return builder.Build();
            });
    }
}
