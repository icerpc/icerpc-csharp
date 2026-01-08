// Copyright (c) ZeroC, Inc.

using IceRpc.Transports;
using IceRpc.Transports.Quic;
using IceRpc.Transports.Slic;
using IceRpc.Transports.Tcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IceRpc.Extensions.DependencyInjection;

/// <summary>Provides an extension method for <see cref="IServiceCollection" /> to add a client connection.</summary>
public static class ClientConnectionServiceCollectionExtensions
{
    /// <summary>Adds a <see cref="ClientConnection" /> and an <see cref="IInvoker" /> to this service collection.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <returns>The service collection.</returns>
    /// <remarks>This method uses the client connection options provided by the <see cref="IOptions{T}" /> of
    /// <see cref="ClientConnectionOptions" />.</remarks>
    /// <example>
    /// The following code adds a ClientConnection singleton to the service collection.
    /// <code source="../../docfx/examples/IceRpc.Extensions.DependencyInjection.Examples/AddIceRpcClientConnectionExamples.cs"
    /// region="ClientConnectionWithOptions" lang="csharp" />
    /// You can also inject a client transport:
    /// <list type="bullet">
    /// <item><description>an <see cref="IDuplexClientTransport" /> for the ice protocol</description></item>
    /// <item><description>an <see cref="IMultiplexedClientTransport" /> for the icerpc protocol</description></item>
    /// </list>
    /// For example, you can add a QUIC client connection as follows:
    /// <code source="../../docfx/examples/IceRpc.Extensions.DependencyInjection.Examples/AddIceRpcClientConnectionExamples.cs"
    /// region="ClientConnectionWithQuic" lang="csharp" />
    /// If you want to customize the options of the default transport (tcp), you just need to inject an
    /// <see cref="IOptions{T}" /> of <see cref="TcpClientTransportOptions" />.
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

        // The default multiplexed transport is QUIC on most platforms.
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsWindows())
        {
#pragma warning disable CA1416 // Validate platform compatibility
            services
                .TryAddSingleton<IMultiplexedClientTransport>(
                    provider => new QuicClientTransport(
                        provider.GetRequiredService<IOptions<QuicClientTransportOptions>>().Value));
#pragma warning restore CA1416 // Validate platform compatibility
        }
        else
        {
            // Use Slic over TCP on other platforms.
            services.
                TryAddSingleton<IMultiplexedClientTransport>(
                    provider => new SlicClientTransport(
                        provider.GetRequiredService<IOptions<SlicTransportOptions>>().Value,
                        provider.GetRequiredService<IDuplexClientTransport>()));
        }

        return services;
    }
}
