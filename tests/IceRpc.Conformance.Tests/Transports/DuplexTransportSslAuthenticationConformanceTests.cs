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

        // The client typically closes the transport connection after receiving AuthenticationException, and the
        // server then fails with an IceRpcException. Some TLS implementations instead report the client's alert as an
        // AuthenticationException during the server handshake.
        Exception? exception = Assert.CatchAsync(
            async () =>
            {
                sut.Client.Dispose();
                await serverConnectTask;
                await sut.Server.ReadAsync(new byte[1], CancellationToken.None);
            });
        Assert.That(
            exception,
            Is.TypeOf<AuthenticationException>()
                .Or.TypeOf<IceRpcException>().And.Property("IceRpcError").EqualTo(IceRpcError.ConnectionAborted)
                .Or.TypeOf<IceRpcException>().And.Property("IceRpcError").EqualTo(IceRpcError.IceRpcError),
            $"The test failed with an unexpected exception {exception}");
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

        // Act/Assert
        Assert.That(async () => await serverConnectTask, Throws.TypeOf<AuthenticationException>());

        // The client handshake typically completes before the server rejects the client certificate: the client only
        // gets an error when it reads or the server closes the connection. Some TLS implementations instead report the
        // server's alert as an AuthenticationException during the client handshake.
        Assert.That(
            async () =>
            {
                await clientConnectTask;
                sut.Server.Dispose();
                await sut.Client.ReadAsync(new byte[1], CancellationToken.None);
            },
            Throws.TypeOf<IceRpcException>().Or.TypeOf<AuthenticationException>());
    }

    /// <summary>Creates the service collection used for the duplex transport conformance tests.</summary>
    protected abstract IServiceCollection CreateServiceCollection();
}
