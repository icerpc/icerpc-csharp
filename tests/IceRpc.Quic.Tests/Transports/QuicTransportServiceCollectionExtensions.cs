// Copyright (c) ZeroC, Inc.

using IceRpc.Tests.Common;
using IceRpc.Transports;
using IceRpc.Transports.Quic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace IceRpc.Tests.Transports;

public static class QuicTransportServiceCollectionExtensions
{
    /// <summary>The loopback server address used by the QUIC tests.</summary>
    /// <remarks>On macOS, the kernel can give the dual-mode listener socket of MsQuic an ephemeral port that an
    /// IPv4-only socket of another process already holds. IPv4 packets sent to that port are then delivered to the
    /// other process and the handshake times out. Connecting over IPv6 avoids this problem. See
    /// https://github.com/icerpc/icerpc-csharp/issues/4714.</remarks>
    public static Uri LoopbackServerAddressUri { get; } =
        new(OperatingSystem.IsMacOS() ? "icerpc://[::1]:0/" : "icerpc://127.0.0.1:0/");

    public static IServiceCollection AddQuicTest(this IServiceCollection services) =>
        services.AddMultiplexedTransportTest(LoopbackServerAddressUri).AddQuicTransport();

    public static IServiceCollection AddQuicTransport(this IServiceCollection services)
    {
        services.AddSingleton(provider => new SslClientAuthenticationOptions
        {
            ClientCertificates =
                    [
                        X509CertificateLoader.LoadPkcs12FromFile(
                            "client.p12",
                            password: null,
                            keyStorageFlags: X509KeyStorageFlags.Exportable)
                    ],
            RemoteCertificateValidationCallback = (sender, certificate, chain, errors) =>
                certificate?.Issuer.Contains("IceRPC Tests CA", StringComparison.Ordinal) ?? false
        })
            .AddSingleton(provider => new SslServerAuthenticationOptions
            {
                ClientCertificateRequired = false,
                ServerCertificate = X509CertificateLoader.LoadPkcs12FromFile(
                    "server.p12",
                    password: null,
                    keyStorageFlags: X509KeyStorageFlags.Exportable)
            })
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
