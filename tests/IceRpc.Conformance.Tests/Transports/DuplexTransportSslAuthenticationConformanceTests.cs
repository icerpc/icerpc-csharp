// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace IceRpc.Conformance.Tests;

/// <summary>Conformance tests to ensure the correct use of the SSL authentication options by the duplex transport
/// implementation. It also checks some basic expected behavior from the SSL implementation.</summary>
public abstract class DuplexTransportSslAuthenticationConformanceTests
{
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

        // The client will typically close the transport connection after receiving AuthenticationException
        var ex = Assert.ThrowsAsync<TransportException>(
            async () =>
            {
                clientConnection.Dispose();
                await serverConnectTask;
                await serverConnection.ReadAsync(new byte[1], CancellationToken.None);
            });
        Assert.That(ex.ErrorCode, Is.EqualTo(TransportErrorCode.ConnectionReset));
    }

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
        var serverConnectTask = serverConnection.ConnectAsync(default);
        await clientConnectTask;

        // Act/Assert
        Assert.That(async () => await serverConnectTask, Throws.TypeOf<AuthenticationException>());

        // The client hanshake terminates before the server, the client doesn't get an error until it reads
        // from the connection.
        Assert.That(
            async () => await clientConnection.ReadAsync(new byte[1], CancellationToken.None),
            Throws.TypeOf<TransportException>());
    }

    /// <summary>Creates the service collection used for the duplex transport conformance tests.</summary>
    protected abstract IServiceCollection CreateServiceCollection();
}
