// Copyright (c) ZeroC, Inc.

using IceRpc.Tests.Common;
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

        var sut = provider.GetRequiredService<ClientServerDuplexConnection>();

        // Start the TLS handshake by calling connect on the client and server connections and wait for the
        // connection establishment.
        var clientConnectTask = sut.Client.ConnectAsync(default);
        var serverConnectTask = sut.AcceptAsync();
        byte[] buffer = new byte[1];

        // Act/Assert
        Assert.That(async () => await clientConnectTask, Throws.TypeOf<AuthenticationException>());

        // The client will typically close the transport connection after receiving AuthenticationException
        var exception = Assert.ThrowsAsync<IceRpcException>(
            async () =>
            {
                sut.Client.Dispose();
                await serverConnectTask;
                await sut.Server.ReadAsync(new byte[1], CancellationToken.None);
            });
        Assert.That(
            exception!.IceRpcError,
            Is.EqualTo(IceRpcError.ConnectionAborted).Or.EqualTo(IceRpcError.IceRpcError),
            $"The test failed with an unexpected IceRpcError {exception}");
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
#pragma warning disable CA5359 // Do Not Disable Certificate Validation, certificate validation is not required for these tests.
                    RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => true
#pragma warning restore CA5359 // Do Not Disable Certificate Validation
                })
            .BuildServiceProvider(validateScopes: true);

        var sut = provider.GetRequiredService<ClientServerDuplexConnection>();

        // Start the TLS handshake by calling connect on the client and server connections and wait for the
        // connection establishment.
        var clientConnectTask = sut.Client.ConnectAsync(default);
        var serverConnectTask = sut.AcceptAsync();
        await clientConnectTask;

        // Act/Assert
        Assert.That(async () => await serverConnectTask, Throws.TypeOf<AuthenticationException>());

        // The client handshake terminates before the server, the client doesn't get an error until it
        // reads or the peer close the connection.
        Assert.That(
            async () =>
            {
                sut.Server.Dispose();
                await sut.Client.ReadAsync(new byte[1], CancellationToken.None);
            },
            Throws.TypeOf<IceRpcException>());
    }

    /// <summary>Creates the service collection used for the duplex transport conformance tests.</summary>
    protected abstract IServiceCollection CreateServiceCollection();
}
