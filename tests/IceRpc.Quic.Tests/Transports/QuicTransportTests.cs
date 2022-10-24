// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using System.Diagnostics;
using System.Net.Quic;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace IceRpc.Tests.Transports;

[Parallelizable(ParallelScope.All)]
[System.Runtime.Versioning.SupportedOSPlatform("macOS")]
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class QuicTransportTests
{
    [OneTimeSetUp]
    public void FixtureSetUp()
    {
        if (!QuicConnection.IsSupported)
        {
            Assert.Ignore("Quic is not supported on this platform");
        }
        Trace.Listeners.Add(new ConsoleTraceListener());
    }

    [Test]
    public async Task Listener_accepts_new_connection_after_client_certificate_validation_callback_rejects_the_connection()
    {
        // Arrange
        int connectionNum = 0;
        IServiceCollection services = new ServiceCollection().AddQuicTest();
        services.AddSingleton(
            new SslClientAuthenticationOptions
            {
                // First connection is rejected, the following connections are accepted.
                RemoteCertificateValidationCallback = (sender, certificate, chain, errors) =>  ++connectionNum > 1,
            });
        await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

        QuicMultiplexedConnection connection1 = CreateClientConnection();
        QuicMultiplexedConnection connection2 = CreateClientConnection();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();

        // Act
        var acceptTask = listener.AcceptAsync(default);

        // Assert
        Assert.That(async () => await connection1.ConnectAsync(default), Throws.TypeOf<TransportException>());
        Assert.That(async () => await connection2.ConnectAsync(default), Throws.Nothing);
        Assert.That(async () => await acceptTask, Throws.Nothing);

        QuicMultiplexedConnection CreateClientConnection() =>
            (QuicMultiplexedConnection)provider.GetRequiredService<IMultiplexedClientTransport>().CreateConnection(
                provider.GetRequiredService<IListener<IMultiplexedConnection>>().ServerAddress,
                provider.GetRequiredService<IOptions<MultiplexedConnectionOptions>>().Value,
                provider.GetRequiredService<SslClientAuthenticationOptions>());
    }

    /// <summary>Verifies that the local certificate selection callback is used to select the client certificate.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Security",
        "CA5359:Do Not Disable Certificate Validation",
        Justification = "Certificate validation is not required for this test")]
    [Test]
    public async Task Tls_client_certificate_selection_callback_called()
    {
        // Arrange
        using var expectedCertificate = new X509Certificate2("../../../certs/client.p12", "password");
        X509Certificate? clientCertificate = null;
        bool localCertificateSelectionCallbackCalled = false;
        IServiceCollection services = new ServiceCollection().AddQuicTest();
        services.AddSingleton(
            new SslServerAuthenticationOptions()
            {
                ServerCertificate = new X509Certificate2("../../../certs/server.p12", "password"),
                ClientCertificateRequired = true,
                RemoteCertificateValidationCallback = (sender, certificate, chain, errors) =>
                {
                    clientCertificate = certificate;
                    return true;
                }
            });

        services.AddSingleton(new SslClientAuthenticationOptions
            {
                LocalCertificateSelectionCallback = (sender, targetHost, localCertificates, remoteCertificate, acceptableIssuers) =>
                {
                    localCertificateSelectionCallbackCalled = true;
                    return expectedCertificate;
                },
                RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => true
            });
        await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

        var clientConnection = provider.GetRequiredService<QuicMultiplexedConnection>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();

        // Act

        // Perform the TLS handshake by calling connect on the client and server connections and wait for the
        // connection establishment.
        Task<TransportConnectionInformation> clientConnectTask = clientConnection.ConnectAsync(default);
        await using IMultiplexedConnection serverConnection = (await listener.AcceptAsync(default)).Connection;
        await serverConnection.ConnectAsync(default);
        await clientConnectTask;

        // Assert
        Assert.That(localCertificateSelectionCallbackCalled, Is.True);
        Assert.That(clientCertificate, Is.Not.Null);
        Assert.That(clientCertificate, Is.EqualTo(expectedCertificate));
    }

    /// <summary>Verifies that the remote certificate validation callbacks set with the client and server connections
    /// are used during the tls handshake.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Security",
        "CA5359:Do Not Disable Certificate Validation",
        Justification = "Certificate validation is not required for this test")]
    [Test]
    public async Task Tls_remote_certificate_validation_callback_called()
    {
        // Arrange
        bool serverCertificateValidationCallback = false;
        bool clientCertificateValidationCallback = false;
        IServiceCollection services = new ServiceCollection().AddQuicTest();
        services.AddSingleton(new SslServerAuthenticationOptions()
            {
                ServerCertificate = new X509Certificate2("../../../certs/server.p12", "password"),
                ClientCertificateRequired = true,
                RemoteCertificateValidationCallback = (sender, certificate, chain, errors) =>
                {
                    serverCertificateValidationCallback = true;
                    return true;
                }
            });

        services.AddSingleton(new SslClientAuthenticationOptions
            {
                ClientCertificates = new X509CertificateCollection()
                {
                    new X509Certificate2("../../../certs/client.p12", "password")
                },
                RemoteCertificateValidationCallback = (sender, certificate, chain, errors) =>
                {
                    clientCertificateValidationCallback = true;
                    return true;
                }
            });

        await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
        var clientConnection = provider.GetRequiredService<QuicMultiplexedConnection>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();

        // Act

        // Perform the TLS handshake by calling connect on the client and server connections and wait for the
        // connection establishment.
        Task<TransportConnectionInformation> clientConnectTask = clientConnection.ConnectAsync(default);
        await using IMultiplexedConnection serverConnection = (await listener.AcceptAsync(default)).Connection;
        await serverConnection.ConnectAsync(default);
        await clientConnectTask;

        // Assert
        Assert.That(serverCertificateValidationCallback, Is.True);
        Assert.That(clientCertificateValidationCallback, Is.True);
    }

    /// <summary>Verifies that the client connection establishment fail with <see cref="AuthenticationException" /> when
    /// the server certificate is not trusted.</summary>
    [Test]
    public async Task Tls_server_certificate_not_trusted()
    {
        // Arrange
        IServiceCollection services = new ServiceCollection().AddQuicTest();
        services.AddSingleton(new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => false
            });
        await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
        var clientConnection = provider.GetRequiredService<QuicMultiplexedConnection>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();

        // Start the TLS handshake by calling connect on the client and server connections and wait for the
        // connection establishment.
        Task<TransportConnectionInformation> clientConnectTask = clientConnection.ConnectAsync(default);
        await using IMultiplexedConnection serverConnection = (await listener.AcceptAsync(default)).Connection;
        await serverConnection.ConnectAsync(default);
        await clientConnectTask;

        // Act/Assert
        Assert.That(async () => await clientConnectTask, Throws.TypeOf<AuthenticationException>());
    }
}
