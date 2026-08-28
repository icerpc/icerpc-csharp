// Copyright (c) ZeroC, Inc.

using IceRpc.Transports;
using IceRpc.Transports.Quic;
using IceRpc.Transports.Tcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Quic;

namespace IceRpc.Extensions.DependencyInjection;

/// <summary>Provides extension methods for <see cref="IServiceCollection" /> to add a client connection.</summary>
public static class ClientConnectionServiceCollectionExtensions
{
    /// <summary>Adds a <see cref="ClientConnection" /> singleton that connects to the specified server address to this
    /// service collection; this singleton is also registered as the <see cref="IInvoker" /> singleton.</summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="serverAddress">The server address of the client connection.</param>
    /// <returns>The service collection.</returns>
    /// <remarks>This method sets <see cref="ClientConnectionOptions.ServerAddress" /> in the client connection options
    /// provided by the <see cref="IOptions{T}" /> of <see cref="ClientConnectionOptions" />; the client connection uses
    /// all the other options from these injected options. If your application connects to multiple servers, call
    /// <see cref="ConnectionCacheServiceCollectionExtensions.AddIceRpcConnectionCache(IServiceCollection)" />
    /// instead.</remarks>
    /// <seealso cref="AddIceRpcClientConnection(IServiceCollection)" />
    public static IServiceCollection AddIceRpcClientConnection(
        this IServiceCollection services,
        ServerAddress serverAddress)
    {
        services.AddOptions<ClientConnectionOptions>().Configure(options => options.ServerAddress = serverAddress);
        return services.AddIceRpcClientConnection();
    }

    /// <summary>Adds a <see cref="ClientConnection" /> singleton that connects to the specified server address URI to
    /// this service collection; this singleton is also registered as the <see cref="IInvoker" /> singleton.</summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="serverAddressUri">The server address URI of the client connection.</param>
    /// <returns>The service collection.</returns>
    /// <remarks>If your application connects to multiple servers, call
    /// <see cref="ConnectionCacheServiceCollectionExtensions.AddIceRpcConnectionCache(IServiceCollection)" />
    /// instead.</remarks>
    /// <seealso cref="AddIceRpcClientConnection(IServiceCollection, ServerAddress)" />
    public static IServiceCollection AddIceRpcClientConnection(this IServiceCollection services, Uri serverAddressUri) =>
        services.AddIceRpcClientConnection(new ServerAddress(serverAddressUri));

    /// <summary>Adds a <see cref="ClientConnection" /> singleton to this service collection; this singleton is also
    /// registered as the <see cref="IInvoker" /> singleton.</summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <returns>The service collection.</returns>
    /// <remarks>This method uses the client connection options provided by the <see cref="IOptions{T}" /> of
    /// <see cref="ClientConnectionOptions" />. If your application connects to multiple servers, call
    /// <see cref="ConnectionCacheServiceCollectionExtensions.AddIceRpcConnectionCache(IServiceCollection)" />
    /// instead.</remarks>
    /// <example>
    /// The following code adds a ClientConnection singleton to the service collection.
    /// <code source="../../docfx/examples/IceRpc.Extensions.DependencyInjection.Examples/AddIceRpcClientConnectionExamples.cs"
    /// region="ClientConnectionWithOptions" lang="csharp" />
    /// You can also inject a client transport:
    /// <list type="bullet">
    /// <item><description>an <see cref="IDuplexClientTransport" /> for the ice protocol</description></item>
    /// <item><description>an <see cref="IMultiplexedClientTransport" /> for the icerpc protocol</description></item>
    /// </list>
    /// For example, you can add a Slic over TCP client connection as follows:
    /// <code source="../../docfx/examples/IceRpc.Extensions.DependencyInjection.Examples/AddIceRpcClientConnectionExamples.cs"
    /// region="ClientConnectionWithSlic" lang="csharp" />
    /// If you want to customize the options of the default multiplexed transport (QUIC), you just need to inject an
    /// <see cref="IOptions{T}" /> of <see cref="QuicClientTransportOptions" />.
    /// </example>
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

    internal static IServiceCollection TryAddIceRpcClientTransport(this IServiceCollection services)
    {
        // The default duplex transport is TCP.
        services
            .AddOptions()
            .TryAddSingleton<IDuplexClientTransport>(
                provider => new TcpClientTransport(
                    provider.GetRequiredService<IOptions<TcpClientTransportOptions>>().Value));

        services
            .TryAddSingleton<IMultiplexedClientTransport>(
                provider =>
                {
                    if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsWindows())
                    {
                        if (QuicConnection.IsSupported)
                        {
                            return new QuicClientTransport(
                                provider.GetRequiredService<IOptions<QuicClientTransportOptions>>().Value);
                        }
                        throw new NotSupportedException(
                            "The default QUIC client transport is not available on this system. Please review the Platform Dependencies for QUIC in the .NET documentation.");
                    }
                    throw new PlatformNotSupportedException(
                        "The default QUIC client transport is not supported on this platform. You need to register an IMultiplexedClientTransport implementation in the service collection.");
                });

        return services;
    }
}
