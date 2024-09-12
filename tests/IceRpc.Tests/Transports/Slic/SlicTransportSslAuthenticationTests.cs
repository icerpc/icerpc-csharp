// Copyright (c) ZeroC, Inc.

using IceRpc.Tests.Common;
using IceRpc.Tests.Transports.Tcp;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace IceRpc.Tests.Transports.Slic;

/// <summary>Test Ssl authentication with Slic transport.</summary>
[Parallelizable(ParallelScope.All)]
public class SlicTransportSslAuthenticationTests
{
    [Test]
    public async Task Slic_over_ssl_client_connection_connect_fails_when_server_provides_untrusted_certificate()
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

        // The connect attempt starts the TLS handshake.
        Task clientConnectTask = sut.Client.ConnectAsync(default);
        await using IMultiplexedConnection serverConnection =
            (await listener.AcceptAsync(default)).Connection;
        var serverConnectTask = serverConnection.ConnectAsync(default);

        // Act/Assert
        Assert.That(async () => await clientConnectTask, Throws.TypeOf<AuthenticationException>());

        // Dispose the client connection here to ensure the server connect task fails promptly.
        await sut.Client.DisposeAsync();
        try
        {
            await serverConnectTask;
        }
        catch
        {
            // Avoid UTE
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Security",
        "CA5359:Do Not Disable Certificate Validation",
        Justification = "The client doesn't need to validate the server certificate for this test")]
    [Test]
    public async Task Slic_over_ssl_server_connection_connect_fails_when_client_provides_untrusted_certificate()
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
        await using IMultiplexedConnection serverConnection =
            (await listener.AcceptAsync(default)).Connection;
        Assert.That(
            async () => await serverConnection.ConnectAsync(default),
            Throws.TypeOf<AuthenticationException>());

        // Dispose the server connection here to ensure the client connect task fails promptly.
        await serverConnection.DisposeAsync();
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
            .AddSlicTransport()
            .AddTcpTransport();
}
