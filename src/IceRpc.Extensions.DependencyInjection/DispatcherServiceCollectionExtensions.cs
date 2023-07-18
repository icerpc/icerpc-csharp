// Copyright (c) ZeroC, Inc.

using IceRpc.Extensions.DependencyInjection.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace IceRpc.Extensions.DependencyInjection;

/// <summary>Provides an extension method for <see cref="IServiceCollection" /> to add a dispatcher.</summary>
public static class DispatcherServiceCollectionExtensions
{
    /// <summary>Adds an <see cref="IDispatcher" /> to this service collection.</summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="configure">The action to configure the new dispatcher.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddIceRpcDispatcher(
        this IServiceCollection services,
        Action<IDispatcherBuilder> configure) =>
        services.AddSingleton(provider =>
        {
            var builder = new DispatcherBuilder(provider);
            configure(builder);
            return builder.Build();
        });
}
