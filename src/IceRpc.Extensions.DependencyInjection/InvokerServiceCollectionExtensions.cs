// Copyright (c) ZeroC, Inc.

using IceRpc.Extensions.DependencyInjection.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace IceRpc.Extensions.DependencyInjection;

/// <summary>Provides an extension method that adds an invoker to an <see cref="IServiceCollection" />.</summary>
public static class InvokerServiceCollectionExtensions
{
    /// <summary>Adds an <see cref="IInvoker" /> to this service collection.</summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="configure">The action to configure the new invoker.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddIceRpcInvoker(
        this IServiceCollection services,
        Action<IInvokerBuilder> configure) =>
        services.AddSingleton(provider =>
            {
                var builder = new InvokerBuilder(provider);
                configure(builder);
                return builder.Build();
            });
}
