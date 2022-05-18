// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace IceRpc.Extensions.DependencyInjection;

/// <summary>This class provide extensions methods to configure <see cref="Server"/> instances.</summary>
public static class ServerBuilderExtensions
{
    /// <summary>Creates a server builder that uses the given service collection.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">The action to configure the server.</param>
    /// <returns>The server builder</returns>
    public static IServiceCollection AddServer(
        this IServiceCollection services,
        Action<ServerBuilder> configure)
    {
        services.AddOptions();
        services.AddSingleton(provider =>
        {
            var builder = new ServerBuilder(services, provider);
            configure(builder);
            return builder.Build();
        });
        return services;
    }
}
