// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace IceRpc.Conformance.Tests;

/// <summary>Conformance tests to ensure the correct use of the SSL authentication options by the multiplexed transport
/// implementation. It also checks some basic expected behavior from the SSL implementation.</summary>
public abstract class MultiplexedTransportSslAuthenticationConformanceTests
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

        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();

        var clientConnection = provider.GetRequiredService<IMultiplexedConnection>();

        // Start the TLS handshake.
        Task clientConnectTask = clientConnection.ConnectAsync(default);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        Task? serverConnectTask = null;
        try
        {
            // We accept two behaviors here:
            // - the listener can internally kill the connection if it's not valid (e.g.: Quic behavior)
            // - the listener can return the connection but ConnectAsync fails(e.g.: Slic behavior)
            (var serverConnection, _) = await listener.AcceptAsync(cts.Token);
            serverConnectTask = serverConnection.ConnectAsync(default);
        }
        catch (OperationCanceledException exception) when (exception.CancellationToken == cts.Token)
        {
            // Expected with Quic
            serverConnectTask = null;
        }

        // Act/Assert
        Assert.That(async () => await clientConnectTask, Throws.TypeOf<AuthenticationException>());

        // We accept two behaviors here:
        // - serverConnectTask is null, the listener internally reject the connection (e.g.: Quic behavior)
        // - the server connect operation fails with either TransportException or IOException (e.g:
        //   Slic behavior), on Windows the underlying SslStream can fail with TransportException or
        //   IOException depending on whether the SSL alert from the peer is detected before than the
        //   peer connection closure.
        if (serverConnectTask is not null)
        {
            // The client will typically close the transport connection after receiving AuthenticationException
            await clientConnection.DisposeAsync();
            Assert.That(
                async () => await serverConnectTask,
                Throws.TypeOf<TransportException>().Or.TypeOf<IOException>());
        }
    }

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

        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();

        var clientConnection = provider.GetRequiredService<IMultiplexedConnection>();

        // Start the TLS handshake by calling connect on the client and server connections and wait for the
        // connection establishment.
        var clientConnectTask = clientConnection.ConnectAsync(default);

        // Act/Assert
        IMultiplexedConnection? serverConnection = null;
        Assert.That(
            async () =>
            {
                // We accept two behaviors here:
                // - the listener can internally kill the client connection if it's not valid (e.g.: Quic behavior)
                // - the listener can return the connection but ConnectAsync fails(e.g.: Slic behavior)
                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
                (serverConnection, _) = await listener.AcceptAsync(cts.Token);
                await serverConnection.ConnectAsync(default);
            },
            Throws.TypeOf<AuthenticationException>().Or.TypeOf<OperationCanceledException>());

        Assert.That(
            async () =>
            {
                // We accept two behaviors here:
                // - the client connection fails with AuthenticationException when try to create a stream
                //   (e.g.: Quic behavior)
                // - the client connect operation fails with either TransportException or IOException (e.g:
                //   Slic behavior), on Windows the underlying SslStream can fail with TransportException or
                //   IOException depending on whether the SSL alert from the peer is detected before than the
                //   peer connection closure.
                if (serverConnection is not null)
                {
                   await serverConnection.DisposeAsync();
                }
                await clientConnectTask;
                var stream = await clientConnection.CreateStreamAsync(bidirectional: false, CancellationToken.None);
                await stream.Output.WriteAsync(new ReadOnlyMemory<byte>(new byte[] { 0xFF }), CancellationToken.None);
            },
            Throws.TypeOf<AuthenticationException>().Or.TypeOf<TransportException>().Or.TypeOf<IOException>());
    }

    /// <summary>Creates the service collection used for the duplex transport conformance tests.</summary>
    protected abstract IServiceCollection CreateServiceCollection();
}
