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
public class SlicTransportSslAuthenticationConformanceTests
{
    [Test]
    public async Task Ssl_client_connection_connect_fails_when_server_provides_untrusted_certificate()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection()
            .AddSingleton(
                new SslServerAuthenticationOptions
                {
                    ServerCertificate = new X509Certificate2("server-untrusted.p12"),
                })
            .AddSingleton(
                new SslClientAuthenticationOptions
                {
                    RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => false
                })
            .BuildServiceProvider(validateScopes: true);

        var sut = provider.GetRequiredService<ClientServerMultiplexedConnection>();

        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();

        // Start the TLS handshake.
        Task clientConnectTask = sut.Client.ConnectAsync(default);
        (IMultiplexedConnection serverConnection, _) = await listener.AcceptAsync(default);
        var serverConnectTask = serverConnection.ConnectAsync(default);

        // Act/Assert
        Assert.That(async () => await clientConnectTask, Throws.TypeOf<AuthenticationException>());

        // The client will typically close the transport connection after receiving AuthenticationException
        await sut.Client.DisposeAsync();
        var exception = Assert.ThrowsAsync<IceRpcException>(async () => await serverConnectTask!);
        Assert.That(
            exception?.IceRpcError,
            Is.EqualTo(IceRpcError.ConnectionAborted).Or.EqualTo(IceRpcError.IceRpcError),
            $"The test failed with an unexpected IceRpcError {exception}");
        await serverConnection.DisposeAsync();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Security",
        "CA5359:Do Not Disable Certificate Validation",
        Justification = "The client doesn't need to validate the server certificate for this test")]
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
                    ServerCertificate = new X509Certificate2("server.p12"),
                })
            .AddSingleton(
                new SslClientAuthenticationOptions
                {
                    ClientCertificates = new X509CertificateCollection()
                    {
                        new X509Certificate2("client-untrusted.p12")
                    },
                    RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => true
                })
            .BuildServiceProvider(validateScopes: true);

        var sut = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();

        // Start the TLS handshake by calling connect on the client and server connections and wait for the
        // connection establishment.
        var clientConnectTask = sut.Client.ConnectAsync(default);

        // Act/Assert
        IMultiplexedConnection? serverConnection = null;
        Assert.That(
            async () =>
            {
                (serverConnection, _) = await listener.AcceptAsync(default);
                await serverConnection.ConnectAsync(default);
            },
            Throws.TypeOf<AuthenticationException>());

        Assert.That(
            async () =>
            {
                if (serverConnection is not null)
                {
                    await serverConnection.DisposeAsync();
                }
                await clientConnectTask;
            },
            Throws.TypeOf<AuthenticationException>().Or.TypeOf<IceRpcException>());
    }

    private static IServiceCollection CreateServiceCollection() =>
        new ServiceCollection()
            .AddMultiplexedTransportTest(new Uri("icerpc://127.0.0.1:0/"))
            .AddSlicTransport()
            .AddTcpTransport();
}
