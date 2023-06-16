// Copyright (c) ZeroC, Inc.

using IceRpc.Tests.Common;
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
                    ServerCertificate = new X509Certificate2("server-untrusted.p12", "password"),
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
        Task? serverConnectTask = null;
        IMultiplexedConnection? serverConnection = null;
        try
        {
            // We accept two behaviors here:
            // - the listener can internally kill the connection if it's not valid (e.g.: Quic behavior)
            // - the listener can return the connection but ConnectAsync fails(e.g.: Slic behavior)
            (serverConnection, _) = await listener.AcceptAsync(default);
            serverConnectTask = serverConnection.ConnectAsync(default);
        }
        catch (AuthenticationException)
        {
            // Expected with Quic
            serverConnectTask = null;
        }

        // Act/Assert
        Assert.That(async () => await clientConnectTask, Throws.TypeOf<AuthenticationException>());

        // We accept two behaviors here:
        // - serverConnectTask is null, the listener internally rejects the connection (e.g.: Quic behavior)
        // - the server connect operation fails with an IceRpcException (e.g: Slic behavior).
        if (serverConnection is not null)
        {
            // The client will typically close the transport connection after receiving AuthenticationException
            await sut.Client.DisposeAsync();
            var exception = Assert.ThrowsAsync<IceRpcException>(async () => await serverConnectTask!);
            Assert.That(
                exception?.IceRpcError,
                Is.EqualTo(IceRpcError.ConnectionAborted).Or.EqualTo(IceRpcError.IceRpcError),
                $"The test failed with an unexpected IceRpcError {exception}");
            await serverConnection.DisposeAsync();
        }
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
                    ServerCertificate = new X509Certificate2("server.p12", "password"),
                })
            .AddSingleton(
                new SslClientAuthenticationOptions
                {
                    ClientCertificates = new X509CertificateCollection()
                    {
                        new X509Certificate2("client-untrusted.p12", "password")
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
                // We accept two behaviors here:
                // - the listener can internally kill the client connection if it's not valid (e.g.: Quic behavior)
                // - the listener can return the connection but ConnectAsync fails (e.g.: Slic behavior)
                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
                (serverConnection, _) = await listener.AcceptAsync(cts.Token);
                await serverConnection.ConnectAsync(default);
            },
            Throws.TypeOf<AuthenticationException>().Or.InstanceOf<OperationCanceledException>());

        Assert.That(
            async () =>
            {
                // We accept two behaviors here:
                // - the client connection fails with AuthenticationException when try to create a stream
                //   (e.g.: Quic behavior)
                // - the client connect operation fails with either TransportException (e.g: Slic behavior).
                if (serverConnection is not null)
                {
                    await serverConnection.DisposeAsync();
                }
                await clientConnectTask;
                var stream = await sut.Client.CreateStreamAsync(bidirectional: false, CancellationToken.None);
                await stream.Output.WriteAsync(new ReadOnlyMemory<byte>(new byte[] { 0xFF }), CancellationToken.None);
            },
            Throws.TypeOf<AuthenticationException>().Or.TypeOf<IceRpcException>());
    }

    /// <summary>Creates the service collection used for the duplex transport conformance tests.</summary>
    protected abstract IServiceCollection CreateServiceCollection();
}
