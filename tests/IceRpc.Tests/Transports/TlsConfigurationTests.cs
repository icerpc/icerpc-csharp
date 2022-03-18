// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace IceRpc.Transports.Tests;

[Parallelizable(scope: ParallelScope.All)]
[Timeout(5000)]
public class TlsConfigurationTests
{
    /// <summary>Verifies that the server connection establishment will fail with <see cref="AuthenticationException"/>
    /// when the client certificate is not trusted.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Security",
        "CA5359:Do Not Disable Certificate Validation",
        Justification = "Certificate validation is not required for this test")]
    [Test]
    public async Task Tls_client_certificate_not_trusted()
    {
        // Arrange
        await using IListener<ISimpleNetworkConnection> listener = CreateTcpListener(
            authenticationOptions: new SslServerAuthenticationOptions()
            {
                ClientCertificateRequired = true,
                RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => false,
                ServerCertificate = new X509Certificate2("../../../certs/server.p12", "password"),
            });

        await using TcpClientNetworkConnection clientConnection = CreateTcpClientConnection(
            listener.Endpoint,
            authenticationOptions: new SslClientAuthenticationOptions
            {
                ClientCertificates = new X509CertificateCollection()
                {
                    new X509Certificate2("../../../certs/client.p12", "password")
                },
                RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => true
            });

        // Start the TLS handshake by calling connect on the client and server connections and wait for the
        // connection establishment.
        Task<NetworkConnectionInformation> clientConnectTask = clientConnection.ConnectAsync(default);
        await using ISimpleNetworkConnection serverConnection = await listener.AcceptAsync();

        // Act/Assert
        Assert.That(
            async () => await serverConnection.ConnectAsync(default),
            Throws.TypeOf<AuthenticationException>());
    }

    /// <summary>Verifies that the local certificate selection callback is used to select the client certificate.
    /// </summary>
    /// <returns></returns>
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
        await using IListener<ISimpleNetworkConnection> listener = CreateTcpListener(
            authenticationOptions: new SslServerAuthenticationOptions()
            {
                ServerCertificate = new X509Certificate2("../../../certs/server.p12", "password"),
                ClientCertificateRequired = true,
                RemoteCertificateValidationCallback = (sender, certificate, chain, errors) =>
                {
                    clientCertificate = certificate;
                    return true;
                }
            });

        await using TcpClientNetworkConnection clientConnection = CreateTcpClientConnection(
            listener.Endpoint,
            authenticationOptions: new SslClientAuthenticationOptions
            {
                LocalCertificateSelectionCallback = (sender, targetHost, localCertificates, remoteCertificate, acceptableIssuers) =>
                {
                    localCertificateSelectionCallbackCalled = true;
                    return expectedCertificate;
                },
                RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => true
            });

        // Act

        // Perform the TLS handshake by calling connect on the client and server connections and wait for the
        // connection establishment.
        Task<NetworkConnectionInformation> clientConnectTask = clientConnection.ConnectAsync(default);
        await using ISimpleNetworkConnection serverConnection = await listener.AcceptAsync();
        await serverConnection.ConnectAsync(default);
        await clientConnectTask;

        // Assert
        Assert.That(localCertificateSelectionCallbackCalled, Is.True);
        Assert.That(clientCertificate, Is.Not.Null);
        Assert.That(clientCertificate, Is.EqualTo(expectedCertificate));
    }

    /// <summary>Verifies that the remote certificate validation callbacks set with the client and server connections
    /// are used during the tls handshake.</summary>
    /// <returns></returns>
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
        await using IListener<ISimpleNetworkConnection> listener = CreateTcpListener(
            authenticationOptions: new SslServerAuthenticationOptions()
            {
                ServerCertificate = new X509Certificate2("../../../certs/server.p12", "password"),
                ClientCertificateRequired = true,
                RemoteCertificateValidationCallback = (sender, certificate, chain, errors) =>
                {
                    serverCertificateValidationCallback = true;
                    return true;
                }
            });

        await using TcpClientNetworkConnection clientConnection = CreateTcpClientConnection(
            listener.Endpoint,
            authenticationOptions: new SslClientAuthenticationOptions
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

        // Act

        // Perform the TLS handshake by calling connect on the client and server connections and wait for the
        // connection establishment.
        Task<NetworkConnectionInformation> clientConnectTask = clientConnection.ConnectAsync(default);
        await using ISimpleNetworkConnection serverConnection = await listener.AcceptAsync();
        await serverConnection.ConnectAsync(default);
        await clientConnectTask;

        // Assert
        Assert.That(serverCertificateValidationCallback, Is.True);
        Assert.That(clientCertificateValidationCallback, Is.True);
    }

    /// <summary>Verifies that the client connection establishment fail with <see cref="AuthenticationException"/> when
    /// the server certificate is not trusted.</summary>
    [Test]
    public async Task Tls_server_certificate_not_trusted()
    {
        // Arrange
        await using IListener<ISimpleNetworkConnection> listener = CreateTcpListener(
            authenticationOptions: new SslServerAuthenticationOptions()
            {
                ServerCertificate = new X509Certificate2("../../../certs/server.p12", "password"),
            });

        await using TcpClientNetworkConnection clientConnection = CreateTcpClientConnection(
            listener.Endpoint,
            authenticationOptions: new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => false
            });

        // Start the TLS handshake by calling connect on the client and server connections and wait for the
        // connection establishment.
        Task<NetworkConnectionInformation> clientConnectTask = clientConnection.ConnectAsync(default);
        await using ISimpleNetworkConnection serverConnection = await listener.AcceptAsync();
        await serverConnection.ConnectAsync(default);

        // Act/Assert
        Assert.That(async () => await clientConnectTask, Throws.TypeOf<AuthenticationException>());
    }

    private static IListener<ISimpleNetworkConnection> CreateTcpListener(
        Endpoint? endpoint = null,
        TcpServerTransportOptions? options = null,
        SslServerAuthenticationOptions? authenticationOptions = null)
    {
        IServerTransport<ISimpleNetworkConnection> serverTransport = new TcpServerTransport(options ?? new());
        return serverTransport.Listen(
            endpoint ?? new Endpoint(Protocol.IceRpc) { Host = "::1", Port = 0 },
            authenticationOptions: authenticationOptions,
            NullLogger.Instance);
    }

    private static TcpClientNetworkConnection CreateTcpClientConnection(
        Endpoint? endpoint = null,
        TcpClientTransportOptions? options = null,
        SslClientAuthenticationOptions? authenticationOptions = null)
    {
        IClientTransport<ISimpleNetworkConnection> transport = new TcpClientTransport(options ?? new());
        return (TcpClientNetworkConnection)transport.CreateConnection(
            endpoint ?? new Endpoint(Protocol.IceRpc),
            authenticationOptions: authenticationOptions,
            NullLogger.Instance);
    }
}
