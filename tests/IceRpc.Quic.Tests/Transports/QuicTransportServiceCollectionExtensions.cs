// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Tests.Common;
using IceRpc.Transports;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net.Security;

namespace IceRpc.Tests.Transports;

[System.Runtime.Versioning.SupportedOSPlatform("macOS")]
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public static class QuicTransportServiceCollectionExtensions
{
    public static IServiceCollection AddQuicTest(this IServiceCollection services)
    {
        services
            .AddSslAuthenticationOptions()
            .AddSingleton(provider =>
                provider.GetRequiredService<IMultiplexedServerTransport>().Listen(
                    new ServerAddress(Protocol.IceRpc) { Host = "127.0.0.1", Port = 0 },
                    provider.GetRequiredService<IOptions<MultiplexedConnectionOptions>>().Value,
                    provider.GetRequiredService<SslServerAuthenticationOptions>(),
                    provider.GetRequiredService<ILogger>() ?? NullLogger.Instance))
            .AddSingleton(provider =>
                (QuicMultiplexedConnection)provider.GetRequiredService<IMultiplexedClientTransport>().CreateConnection(
                    provider.GetRequiredService<IListener<IMultiplexedConnection>>().ServerAddress,
                    provider.GetRequiredService<IOptions<MultiplexedConnectionOptions>>().Value,
                    provider.GetRequiredService<SslClientAuthenticationOptions>()))
            .AddSingleton<IMultiplexedServerTransport>(provider =>
                new QuicServerTransport(
                    provider.GetRequiredService<IOptionsMonitor<QuicServerTransportOptions>>().Get("server")))
            .AddSingleton<IMultiplexedClientTransport>(provider =>
                new QuicClientTransport(
                    provider.GetRequiredService<IOptionsMonitor<QuicClientTransportOptions>>().Get("client")));

        services.AddOptions<QuicServerTransportOptions>("client");
        services.AddOptions<QuicClientTransportOptions>("server");

        return services;
    }
}
