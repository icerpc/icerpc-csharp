// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace IceRpc.Conformance.Tests;

/// <summary>Conformance tests for the duplex transports SslAuthentication.</summary>
public abstract class DuplexTransportSslAuthenticationConformanceTests
{
    /// <summary>Verifies that the server connection establishment will fail with <see cref="AuthenticationException" />
    /// when the client certificate is not trusted.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Security",
        "CA5359:Do Not Disable Certificate Validation",
        Justification = "Certificate validation is not required for this test")]
    [Test]
    public async Task Ssl_server_connection_connect_fails_when_client_provides_untrusted_certificate()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection()
            .AddSingleton(
                new SslServerAuthenticationOptions
                {
                    ClientCertificateRequired = true,
                    RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => false,
                    ServerCertificate = new X509Certificate2("../../../certs/server.p12", "password"),
                })
            .AddSingleton(
                new SslClientAuthenticationOptions
                {
                    ClientCertificates = new X509CertificateCollection()
                    {
                        new X509Certificate2("../../../certs/client.p12", "password")
                    },
                    RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => true
                })
            .BuildServiceProvider(validateScopes: true);

        var listener = provider.GetRequiredService<IListener<IDuplexConnection>>();

        var clientConnection = provider.GetRequiredService<IDuplexConnection>();

        // Start the TLS handshake by calling connect on the client and server connections and wait for the
        // connection establishment.
        var clientConnectTask = clientConnection.ConnectAsync(default);
        using IDuplexConnection serverConnection = (await listener.AcceptAsync(default)).Connection;

        // Act/Assert
        Assert.That(
            async () => await serverConnection.ConnectAsync(default),
            Throws.TypeOf<AuthenticationException>());
        Assert.That(async () => await clientConnectTask, Throws.Nothing);
        Assert.That(
            async () =>
            {
                await serverConnection.WriteAsync(
                    new List<ReadOnlyMemory<byte>>() { new byte[1] },
                    CancellationToken.None);
                byte[] buffer = new byte[1];
                await clientConnection.ReadAsync(buffer, CancellationToken.None);
            },
            Throws.TypeOf<TransportException>());
    }

    /// <summary>Verifies that the server connection establishment will fail with <see cref="AuthenticationException" />
    /// when the client certificate is not trusted.</summary>
    [Test]
    public async Task Ssl_client_connection_connect_fails_when_server_provides_untrusted_certificate()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection()
            .AddSingleton(
                new SslServerAuthenticationOptions
                {
                    ServerCertificate = new X509Certificate2("../../../certs/server.p12", "password"),
                })
            .AddSingleton(
                new SslClientAuthenticationOptions
                {
                    RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => false
                })
            .BuildServiceProvider(validateScopes: true);

        var listener = provider.GetRequiredService<IListener<IDuplexConnection>>();

        var clientConnection = provider.GetRequiredService<IDuplexConnection>();

        // Start the TLS handshake by calling connect on the client and server connections and wait for the
        // connection establishment.
        var clientConnectTask = clientConnection.ConnectAsync(default);
        (IDuplexConnection serverConnection, _) = await listener.AcceptAsync(default);
        var serverConnectTask = serverConnection.ConnectAsync(default);
        byte[] buffer = new byte[1];

        // Act/Assert
        Assert.That(async () => await clientConnectTask, Throws.TypeOf<AuthenticationException>());
        Assert.That(async () => await serverConnectTask, Throws.Nothing);
        Assert.That(
            async () => await serverConnection.ReadAsync(buffer, CancellationToken.None),
            Throws.TypeOf<TransportException>());
    }

    /// <summary>Creates the service collection used for the duplex transport conformance tests.</summary>
    protected abstract IServiceCollection CreateServiceCollection();
}
