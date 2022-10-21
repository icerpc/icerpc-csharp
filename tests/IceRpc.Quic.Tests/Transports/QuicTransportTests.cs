// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using System.Diagnostics;
using System.Net.Quic;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

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

    [Test]
    public async Task Connection_connec_fails_when_server_rejects_the_certificate()
    {
        // Arrange
        IServiceCollection services = new ServiceCollection().AddQuicTest();
        var sslServerAuthenticationOptions = new SslServerAuthenticationOptions
        {
            ClientCertificateRequired = true,
            ServerCertificate = new X509Certificate2("../../../certs/server.p12", "password"),
            // Reject the client certificate
            RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => false,
        };
        var clientAuthenticationOptions = new SslClientAuthenticationOptions
        {
            ClientCertificates = new X509CertificateCollection
            {
                new X509Certificate2("../../../certs/client.p12", "password")
            },
            // First connection is rejected, the following connections are accepted.
            RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => true,
        };
        await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

        var serverTransport = new QuicServerTransport();
        var clientTransport = new QuicClientTransport();
        var options = new MultiplexedConnectionOptions();
        options.StreamErrorCodeConverter = IceRpcProtocol.Instance.MultiplexedStreamErrorCodeConverter;

        var listener = serverTransport.Listen(
            new ServerAddress(Protocol.IceRpc) { Host = "127.0.0.1", Port = 0 },
            options,
            sslServerAuthenticationOptions);
        QuicMultiplexedConnection connection1 = CreateClientConnection();

        // Act
        var acceptTask = listener.AcceptAsync(default);

        // Assert
        Assert.That(
            async () => await connection1.ConnectAsync(default),
            Throws.TypeOf<TransportException>());

        QuicMultiplexedConnection CreateClientConnection() =>
            (QuicMultiplexedConnection)clientTransport.CreateConnection(
                listener.ServerAddress,
                options,
                clientAuthenticationOptions);
    }
}
