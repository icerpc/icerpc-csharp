// Copyright (c) ZeroC, Inc.

using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IceRpc.Extensions.DependencyInjection;

/// <summary>Provides an extension method that adds a connection cache to an <see cref="IServiceCollection" />.
/// </summary>
public static class ConnectionCacheServiceCollectionExtensions
{
    /// <summary>Adds a <see cref="ConnectionCache" /> and an <see cref="IInvoker" /> to this service collection.
    /// </summary>
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
}
