// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace IceRpc.Extensions.DependencyInjection;

/// <summary>This class provide extensions methods to configure <see cref="Server"/> instances.</summary>
public static class ServerBuilderExtensions
{
    /// <summary>Creates a server builder that uses the given service collection.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddIceRpcServer(this IServiceCollection services)
    {
        services.AddSingleton(provider => new ServerBuilder(provider).Build());
        return services;
    }

    /// <summary>Creates a server builder that uses the given service collection.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">The action to configure the server.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddIceRpcServer(
        this IServiceCollection services,
        Action<ServerBuilder> configure)
    {
        services.AddSingleton(provider =>
        {
            var builder = new ServerBuilder(provider);
            configure(builder);
            return builder.Build();
        });
        return services;
    }

    /// <summary>Creates a router builder that uses the given service collection.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">The action to configure the router.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddRouter(
        this IServiceCollection services,
        Action<RouterBuilder> configure)
    {
        services.AddSingleton<IDispatcher>(provider =>
        {
            var builder = new RouterBuilder(provider);
            configure(builder);
            return builder.Build();
        });
        return services;
    }
}
