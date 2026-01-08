// Copyright (c) ZeroC, Inc.

using IceRpc.Transports;
using IceRpc.Transports.Tcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IceRpc.Extensions.DependencyInjection;

/// <summary>Provides an extension method for <see cref="IServiceCollection" /> to add a connection cache.</summary>
public static class ConnectionCacheServiceCollectionExtensions
{
    /// <summary>Adds a <see cref="ConnectionCache" /> and an <see cref="IInvoker" /> to this service collection.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <returns>The service collection.</returns>
    /// <remarks>This method uses the connection cache options provided by the <see cref="IOptions{T}" /> of
    /// <see cref="ConnectionCacheOptions" />.</remarks>
    /// <example>
    /// The following code adds a ConnectionCache singleton to the service collection.
    /// <code source="../../docfx/examples/IceRpc.Extensions.DependencyInjection.Examples/AddIceRpcConnectionCacheExamples.cs"
    /// region="DefaultConnectionCache" lang="csharp" />
    /// The resulting singleton is a default connection cache. If you want to customize this connection cache, add an
    /// <see cref="IOptions{T}" /> of <see cref="ConnectionCacheOptions" /> to your DI container:
    /// <code source="../../docfx/examples/IceRpc.Extensions.DependencyInjection.Examples/AddIceRpcConnectionCacheExamples.cs"
    /// region="ConnectionCacheWithOptions" lang="csharp" />
    /// You can also inject a client transport:
    /// <list type="bullet">
    /// <item><description>an <see cref="IDuplexClientTransport" /> for the ice protocol</description></item>
    /// <item><description>an <see cref="IMultiplexedClientTransport" /> for the icerpc protocol</description></item>
    /// </list>
    /// The following example shows a connection cache that uses Slic over TCP for icerpc connections and keeps the
    /// default duplex transport (TCP) for ice connections.
    /// <code source="../../docfx/examples/IceRpc.Extensions.DependencyInjection.Examples/AddIceRpcConnectionCacheExamples.cs"
    /// region="ConnectionCacheWithSlic"
    /// lang="csharp" />
    /// If you want to customize the options of the default duplex transport (TCP), you just need to inject an
    /// <see cref="IOptions{T}" /> of <see cref="TcpClientTransportOptions" />.
    /// </example>
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
