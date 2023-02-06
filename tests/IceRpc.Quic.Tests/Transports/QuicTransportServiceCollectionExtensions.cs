// Copyright (c) ZeroC, Inc.

using IceRpc.Tests.Common;
using IceRpc.Transports;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace IceRpc.Tests.Transports;

public static class QuicTransportServiceCollectionExtensions
{
    public static IServiceCollection AddQuicTest(this IServiceCollection services)
    {
        services
            .AddSingleton(provider => new SslClientAuthenticationOptions
            {
                ClientCertificates = new X509CertificateCollection
                    {
                        new X509Certificate2("../../../certs/client.p12", "password")
                    },
                RemoteCertificateValidationCallback = (sender, certificate, chain, errors) =>
                    certificate?.Issuer.Contains("Ice Tests CA", StringComparison.Ordinal) ?? false
            })
            .AddSingleton(provider => new SslServerAuthenticationOptions
            {
                ClientCertificateRequired = false,
                ServerCertificate = new X509Certificate2("../../../certs/server.p12", "password")
            })
            .AddSingleton(provider =>
                provider.GetRequiredService<IMultiplexedServerTransport>().Listen(
                    new ServerAddress(Protocol.IceRpc) { Host = "127.0.0.1", Port = 0 },
                    provider.GetRequiredService<IOptions<MultiplexedConnectionOptions>>().Value,
                    provider.GetRequiredService<SslServerAuthenticationOptions>()))
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
