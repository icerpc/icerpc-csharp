// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace IceRpc.Tests.Transports;

[Parallelizable(ParallelScope.All)]
[System.Runtime.Versioning.SupportedOSPlatform("macOS")]
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class QuicTransportTests
{
    [Test]
    public async Task Listener_accepts_new_connection_after_client_certificate_validation_callback_rejects_the_connection()
    {
        // Arrange
        int connectionNum = 0;
        IServiceCollection services = new ServiceCollection().AddQuicTest();
        services.AddSingleton(
            new SslServerAuthenticationOptions
            {
                ServerCertificate = new X509Certificate2("../../../certs/server.p12", "password")
            });
        services.AddSingleton(
            new SslClientAuthenticationOptions
            {
                // First connection is rejected, following connections are accepted
                RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => ++connectionNum > 1,
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
    public async Task Listener_accepts_new_connection_after_server_certificate_validation_callback_rejects_the_connection()
    {
        // Arrange
        //int connectionNum = 0;
        IServiceCollection services = new ServiceCollection().AddQuicTest();
        services.AddSingleton(
            new SslServerAuthenticationOptions
            {
                ClientCertificateRequired = true,
                ServerCertificate = new X509Certificate2("../../../certs/server.p12", "password"),
                // First connection is rejected, following connections are accepted
                RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => {
                    System.Diagnostics.Debug.Assert(false);
                    return false;
                },
            });
        services.AddSingleton(
            new SslClientAuthenticationOptions
            {
                /*ClientCertificates = new X509CertificateCollection()
                {
                    new X509Certificate2("../../../certs/client.p12", "password")
                },*/
                RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => true,
            });
        await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

        QuicMultiplexedConnection connection1 = CreateClientConnection();
        QuicMultiplexedConnection connection2 = CreateClientConnection();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();

        // Act
        var acceptTask = listener.AcceptAsync(default);

        // Assert
        Assert.That(async () => await connection1.ConnectAsync(default), Throws.TypeOf<TransportException>());

        QuicMultiplexedConnection CreateClientConnection() =>
            (QuicMultiplexedConnection)provider.GetRequiredService<IMultiplexedClientTransport>().CreateConnection(
                provider.GetRequiredService<IListener<IMultiplexedConnection>>().ServerAddress,
                provider.GetRequiredService<IOptions<MultiplexedConnectionOptions>>().Value,
                provider.GetRequiredService<SslClientAuthenticationOptions>());
    }
}
