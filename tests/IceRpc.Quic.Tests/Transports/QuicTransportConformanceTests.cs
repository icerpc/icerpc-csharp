// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Conformance.Tests;
using IceRpc.Internal;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using System.Net.Quic;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace IceRpc.Tests.Transports;

[Parallelizable(ParallelScope.All)]
[System.Runtime.Versioning.SupportedOSPlatform("macOS")]
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class QuicTransportConformanceTests : MultiplexedTransportConformanceTests
{
    [OneTimeSetUp]
    public void FixtureSetUp()
    {
        if (!QuicConnection.IsSupported)
        {
            Assert.Ignore("Quic is not supported on this platform");
        }
    }

    /// <summary>Creates the service collection used for Quic multiplexed transports for conformance testing.</summary>
    protected override IServiceCollection CreateServiceCollection()
    {
        IServiceCollection services = new ServiceCollection()
            .AddSingleton<IMultiplexedServerTransport>(provider => new QuicServerTransport(
                provider.GetRequiredService<IOptions<QuicServerTransportOptions>>().Value))
            .AddSingleton<IMultiplexedClientTransport>(provider => new QuicClientTransport(
                provider.GetRequiredService<IOptions<QuicClientTransportOptions>>().Value))
            .AddSingleton(provider =>
                provider.GetRequiredService<IMultiplexedServerTransport>().Listen(
                    new ServerAddress(Protocol.IceRpc) { Host = "127.0.0.1", Port = 0 },
                    provider.GetRequiredService<IOptions<MultiplexedConnectionOptions>>().Value,
                    provider.GetRequiredService<SslServerAuthenticationOptions>()))
            .AddSingleton(provider =>
                new SslClientAuthenticationOptions
                {
                    ClientCertificates = new X509CertificateCollection()
                        {
                            new X509Certificate2("../../../certs/client.p12", "password")
                        },
                    RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => true
                })
            .AddSingleton(provider => new SslServerAuthenticationOptions
            {
                ClientCertificateRequired = false,
                ServerCertificate = new X509Certificate2("../../../certs/server.p12", "password")
            });

        services.AddOptions<QuicServerTransportOptions>();
        services.AddOptions<QuicClientTransportOptions>();
        services.AddOptions<MultiplexedConnectionOptions>().Configure(
            options => options.PayloadErrorConverter = IceRpcProtocol.Instance.PayloadErrorCodeConverter);

        return services;
    }
}
