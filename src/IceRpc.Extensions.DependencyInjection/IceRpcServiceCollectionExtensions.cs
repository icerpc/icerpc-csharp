// Copyright (c) ZeroC, Inc.

using IceRpc.Extensions.DependencyInjection.Internal;
using IceRpc.Transports;
using IceRpc.Transports.Slic;
using IceRpc.Transports.Tcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IceRpc.Extensions.DependencyInjection;

/// <summary>Provides extension methods for setting up IceRPC services in an <see cref="IServiceCollection" />.
/// </summary>
public static class IceRpcServiceCollectionExtensions
{
    /// <summary>Adds a <see cref="ClientConnection" /> and an <see cref="IInvoker" /> singleton to this service
    /// collection.</summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddIceRpcClientConnection(this IServiceCollection services) =>
        services
            .TryAddIceRpcClientTransport()
            .AddSingleton(provider =>
                new ClientConnection(
                    provider.GetRequiredService<IOptions<ClientConnectionOptions>>().Value,
                    provider.GetRequiredService<IDuplexClientTransport>(),
                    provider.GetRequiredService<IMultiplexedClientTransport>(),
                    provider.GetService<ILogger<ClientConnection>>()))
            .AddSingleton<IInvoker>(provider => provider.GetRequiredService<ClientConnection>());

    /// <summary>Adds a <see cref="ConnectionCache" /> and an <see cref="IInvoker" /> singleton to this service
    /// collection.</summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddIceRpcConnectionCache(this IServiceCollection services) =>
        services
            .TryAddIceRpcClientTransport()
            .AddSingleton(provider =>
                new ConnectionCache(
                    provider.GetRequiredService<IOptions<ConnectionCacheOptions>>().Value,
                    provider.GetRequiredService<IDuplexClientTransport>(),
                    provider.GetRequiredService<IMultiplexedClientTransport>(),
                    provider.GetService<ILogger<ConnectionCache>>()))
            .AddSingleton<IInvoker>(provider => provider.GetRequiredService<ConnectionCache>());

    /// <summary>Adds an <see cref="IDispatcher" /> singleton to this service collection using a builder.</summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="configure">The action to configure the dispatcher builder.</param>
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

    /// <summary>Adds an <see cref="IInvoker" /> singleton to this service collection using a builder.</summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="configure">The action to configure the invoker builder.</param>
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

    private static IServiceCollection TryAddIceRpcClientTransport(this IServiceCollection services)
    {
        services
            .AddOptions()
            .TryAddSingleton<IDuplexClientTransport>(
                provider => new TcpClientTransport(
                    provider.GetRequiredService<IOptions<TcpClientTransportOptions>>().Value));

        services.
            TryAddSingleton<IMultiplexedClientTransport>(
                provider => new SlicClientTransport(
                    provider.GetRequiredService<IOptions<SlicTransportOptions>>().Value,
                    provider.GetRequiredService<IDuplexClientTransport>()));

        return services;
    }
}
