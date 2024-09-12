// Copyright (c) ZeroC, Inc.

using IceRpc.Tests.Common;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Net.Quic;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace IceRpc.Tests.Transports;

/// <summary>Test Ssl authentication with Quic transport.</summary>
[NonParallelizable]
public class QuicTransportSslAuthenticationTests
{
    [OneTimeSetUp]
    public void FixtureSetUp()
    {
        if (!QuicConnection.IsSupported)
        {
            Assert.Ignore("Quic is not supported on this platform");
        }
    }

    [Test]
    public async Task Quic_client_connection_connect_fails_when_server_provides_untrusted_certificate()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection()
            .AddSingleton(
                new SslServerAuthenticationOptions
                {
                    ServerCertificate = X509CertificateLoader.LoadPkcs12FromFile(
                        "server-untrusted.p12",
                        password: null,
                        keyStorageFlags: X509KeyStorageFlags.Exportable),
                })
            .AddSingleton(
                new SslClientAuthenticationOptions
                {
                    RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => false
                })
            .BuildServiceProvider(validateScopes: true);

        var sut = provider.GetRequiredService<ClientServerMultiplexedConnection>();

        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();

        // Act/Assert

        // The connect attempt starts the TLS handshake.
        Assert.That(
            async () => await sut.Client.ConnectAsync(default),
            Throws.TypeOf<AuthenticationException>());
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Security",
        "CA5359:Do Not Disable Certificate Validation",
        Justification = "The client doesn't need to validate the server certificate for this test")]
    [Test]
    public async Task Quic_server_connection_connect_fails_when_client_provides_untrusted_certificate()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection()
            .AddSingleton(
                new SslServerAuthenticationOptions
                {
                    ClientCertificateRequired = true,
                    RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => false,
                    ServerCertificate = X509CertificateLoader.LoadPkcs12FromFile(
                        "server.p12",
                        password: null,
                        keyStorageFlags: X509KeyStorageFlags.Exportable),
                })
            .AddSingleton(
                new SslClientAuthenticationOptions
                {
                    ClientCertificates =
                    [
                        X509CertificateLoader.LoadPkcs12FromFile(
                            "client-untrusted.p12",
                            password: null,
                            keyStorageFlags: X509KeyStorageFlags.Exportable)
                    ],
                    RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => true
                })
            .BuildServiceProvider(validateScopes: true);

        var sut = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();

        // The connect attempt starts the TLS handshake.
        var clientConnectTask = sut.Client.ConnectAsync(default);

        // Act/Assert
        Assert.That(
            async () => await listener.AcceptAsync(default),
            Throws.InstanceOf<AuthenticationException>());

        try
        {
            await clientConnectTask;
        }
        catch
        {
            // Avoid UTE
        }
    }

    private static IServiceCollection CreateServiceCollection() =>
        new ServiceCollection()
            .AddMultiplexedTransportTest(new Uri("icerpc://127.0.0.1:0/"))
            .AddQuicTransport();
}
