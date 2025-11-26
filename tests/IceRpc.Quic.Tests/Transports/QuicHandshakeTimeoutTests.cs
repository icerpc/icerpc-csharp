// Copyright (c) ZeroC, Inc.

using IceRpc.Tests.Common;
using IceRpc.Transports;
using IceRpc.Transports.Quic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using System.Net.Quic;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace IceRpc.Tests.Transports;

[NonParallelizable]
public class QuicHandshakeTimeoutTests
{
    [OneTimeSetUp]
    public void FixtureSetUp()
    {
        if (!QuicConnection.IsSupported)
        {
            Assert.Ignore("Quic is not supported on this platform");
        }
    }

    /// <summary>Verifies that a successful connection can be established when the handshake completes within the
    /// configured timeout.</summary>
    [Test]
    public async Task Quic_connection_succeeds_when_handshake_completes_within_timeout()
    {
        // Arrange
        var services = new ServiceCollection().AddQuicTest();

        // Configure a generous handshake timeout to ensure success
        services.AddOptions<MultiplexedConnectionOptions>().Configure(
            options => options.HandshakeTimeout = TimeSpan.FromSeconds(30));

        await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

        var clientServerConnection = provider.GetRequiredService<ClientServerMultiplexedConnection>();

        // Act/Assert
        // The connection should succeed within the configured timeout
        await clientServerConnection.AcceptAndConnectAsync();
    }

    /// <summary>Verifies that a connection fails when the handshake does not complete within the configured
    /// timeout.</summary>
    /// <remarks>This test uses a server that delays the handshake by not accepting connections.</remarks>
    [Test]
    public async Task Quic_connection_fails_when_handshake_exceeds_timeout()
    {
        // Arrange
        var shortTimeout = TimeSpan.FromMilliseconds(500);

        var services = new ServiceCollection();

        // Configure a very short handshake timeout
        services.AddOptions<MultiplexedConnectionOptions>().Configure(
            options => options.HandshakeTimeout = shortTimeout);

        // Add SSL options (required for QUIC)
        services.AddSingleton(provider => new SslClientAuthenticationOptions
            {
                ClientCertificates =
                    [
                        X509CertificateLoader.LoadPkcs12FromFile(
                            "client.p12",
                            password: null,
                            keyStorageFlags: X509KeyStorageFlags.Exportable)
                    ],
                RemoteCertificateValidationCallback = (sender, certificate, chain, errors) =>
                    certificate?.Issuer.Contains("IceRPC Tests CA", StringComparison.Ordinal) ?? false
            })
            .AddSingleton(provider => new SslServerAuthenticationOptions
            {
                ClientCertificateRequired = false,
                ServerCertificate = X509CertificateLoader.LoadPkcs12FromFile(
                    "server.p12",
                    password: null,
                    keyStorageFlags: X509KeyStorageFlags.Exportable)
            });

        services.AddSingleton<IMultiplexedServerTransport>(provider =>
                new QuicServerTransport(
                    provider.GetRequiredService<IOptionsMonitor<QuicServerTransportOptions>>().Get("server")))
            .AddSingleton<IMultiplexedClientTransport>(provider =>
                new QuicClientTransport(
                    provider.GetRequiredService<IOptionsMonitor<QuicClientTransportOptions>>().Get("client")));

        services.AddOptions<QuicServerTransportOptions>("server");
        services.AddOptions<QuicClientTransportOptions>("client");

        // Add listener (server side)
        services.AddSingleton(provider =>
            provider.GetRequiredService<IMultiplexedServerTransport>().Listen(
                new ServerAddress(new Uri("icerpc://127.0.0.1:0/")),
                provider.GetService<IOptions<MultiplexedConnectionOptions>>()?.Value ?? new(),
                serverAuthenticationOptions: provider.GetService<SslServerAuthenticationOptions>()));

        // Add client connection
        services.AddSingleton(provider =>
        {
            var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
            var clientTransport = provider.GetRequiredService<IMultiplexedClientTransport>();
            return clientTransport.CreateConnection(
                listener.ServerAddress,
                provider.GetService<IOptions<MultiplexedConnectionOptions>>()?.Value ?? new(),
                provider.GetService<SslClientAuthenticationOptions>());
        });

        await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        var clientConnection = provider.GetRequiredService<IMultiplexedConnection>();

        try
        {
            var startTime = TimeSpan.FromMilliseconds(Environment.TickCount64);

            // Act - Attempt to connect without accepting on the server side
            // The handshake should timeout because the server is not completing the handshake
            Assert.That(
                async () => await clientConnection.ConnectAsync(CancellationToken.None),
                Throws.InstanceOf<IceRpcException>());

            // Assert - Verify timeout occurred in a reasonable time frame
            Assert.That(
                TimeSpan.FromMilliseconds(Environment.TickCount64) - startTime,
                Is.GreaterThan(shortTimeout - TimeSpan.FromMilliseconds(100)));
        }
        finally
        {
            await clientConnection.DisposeAsync();
            await listener.DisposeAsync();
        }
    }
}
