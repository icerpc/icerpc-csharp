// Copyright (c) ZeroC, Inc.

using IceRpc.Tests.Common;
using IceRpc.Transports;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Net.Security;

namespace IceRpc.Tests.Transports;

public static class SlicTransportServiceCollectionExtensions
{
    public static IServiceCollection AddSlicTest(this IServiceCollection services)
    {
        services
            .AddColocTransport()
            .AddSingleton(provider =>
                provider.GetRequiredService<IMultiplexedServerTransport>().Listen(
                    new ServerAddress(Protocol.IceRpc) { Host = "colochost" },
                    provider.GetRequiredService<IOptions<MultiplexedConnectionOptions>>().Value,
                    provider.GetService<SslServerAuthenticationOptions>()))
            .AddSingleton(provider =>
                (SlicConnection)provider.GetRequiredService<IMultiplexedClientTransport>().CreateConnection(
                    provider.GetRequiredService<IListener<IMultiplexedConnection>>().ServerAddress,
                    provider.GetRequiredService<IOptions<MultiplexedConnectionOptions>>().Value,
                    provider.GetService<SslClientAuthenticationOptions>()))
            .AddSingleton<IMultiplexedServerTransport>(provider =>
                new SlicServerTransport(
                    provider.GetRequiredService<IOptionsMonitor<SlicTransportOptions>>().Get("server"),
                    provider.GetRequiredService<IDuplexServerTransport>()))
            .AddSingleton<IMultiplexedClientTransport>(provider =>
                new SlicClientTransport(
                    provider.GetRequiredService<IOptionsMonitor<SlicTransportOptions>>().Get("client"),
                    provider.GetRequiredService<IDuplexClientTransport>()))
            .AddSingleton(provider =>
                new ClientServerMultiplexedConnection(
                    provider.GetRequiredService<SlicConnection>(),
                    provider.GetRequiredService<IListener<IMultiplexedConnection>>()));

        services.AddOptions<SlicTransportOptions>("client");
        services.AddOptions<SlicTransportOptions>("server");

        return services;
    }
}
