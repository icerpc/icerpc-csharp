// Copyright (c) ZeroC, Inc.

using IceRpc.Conformance.Tests;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using System.Net.Quic;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace IceRpc.Tests.Transports;

[Parallelizable(ParallelScope.All)]
public class QuicConnectionConformanceTests : MultiplexedConnectionConformanceTests
{
    [OneTimeSetUp]
    public void FixtureSetUp() => QuicTransportConformanceTestsServiceCollection.SetUp();

    /// <summary>Creates the service collection used for Quic connection conformance testing.</summary>
    protected override IServiceCollection CreateServiceCollection() => new ServiceCollection().UseQuic();
}

[Parallelizable(ParallelScope.All)]
[System.Runtime.Versioning.SupportedOSPlatform("macOS")]
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class QuicStreamConformanceTests : MultiplexedStreamConformanceTests
{
    [OneTimeSetUp]
    public void FixtureSetUp() => QuicTransportConformanceTestsServiceCollection.SetUp();

    /// <summary>Creates the service collection used for Quic stream conformance testing.</summary>
    protected override IServiceCollection CreateServiceCollection() => new ServiceCollection().UseQuic();
}

[Parallelizable(ParallelScope.All)]
public class QuicListenerConformanceTests : MultiplexedListenerConformanceTests
{
    [OneTimeSetUp]
    public void FixtureSetUp() => QuicTransportConformanceTestsServiceCollection.SetUp();

    /// <summary>Creates the service collection used for Quic listener conformance testing.</summary>
    protected override IServiceCollection CreateServiceCollection() => new ServiceCollection().UseQuic();
}

internal static class QuicTransportConformanceTestsServiceCollection
{
    internal static void SetUp()
    {
        if (!QuicConnection.IsSupported)
        {
            Assert.Ignore("Quic is not supported on this platform");
        }
    }

    internal static IServiceCollection UseQuic(this IServiceCollection serviceCollection)
    {
        IServiceCollection services = serviceCollection
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
#pragma warning disable CA5359 // Do Not Disable Certificate Validation, certificate validation is not required for these tests.
                    RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => true
#pragma warning restore CA5359 // Do Not Disable Certificate Validation
                })
            .AddSingleton(provider => new SslServerAuthenticationOptions
            {
                ClientCertificateRequired = false,
                ServerCertificate = new X509Certificate2("../../../certs/server.p12", "password")
            });

        services.AddOptions<QuicServerTransportOptions>();
        services.AddOptions<QuicClientTransportOptions>();

        return services;
    }
}
