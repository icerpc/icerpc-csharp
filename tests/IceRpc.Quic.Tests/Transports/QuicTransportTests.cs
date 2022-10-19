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
    /// <summary>Verififes that the QuicListener doesn't stop accepting connections, when an accept call fails
    /// </summary>
    [Test]
    public async Task Accept_continues_after_connection_rejection_failure()
    {
        // Arrange
        var clientValidationCallback = new CertificateValidationCallback();
        IServiceCollection services = new ServiceCollection().AddQuicTest();
        services.AddSingleton(
            new SslServerAuthenticationOptions
            {
                ServerCertificate = new X509Certificate2("../../../certs/server.p12", "password"),
                RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => true,
            });
        services.AddSingleton(
            new SslClientAuthenticationOptions
            {
                ClientCertificates = new X509CertificateCollection()
                {
                    new X509Certificate2("../../../certs/client.p12", "password")
                },
                RemoteCertificateValidationCallback =
                    (sender, certificate, chain, errors) => clientValidationCallback.Validate(),
            });
        await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

        QuicMultiplexedConnection connection1 = CreateClientConnection();
        QuicMultiplexedConnection connection2 = CreateClientConnection();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();

        // Act/Assert
        var connectTask = connection1.ConnectAsync(default);
        var acceptTask = listener.AcceptAsync(default);

        Assert.That(async () => await connectTask, Throws.TypeOf<TransportException>());
        clientValidationCallback.Result = true; // Allow next connection to success
        Assert.That(async () => await connection2.ConnectAsync(default), Throws.Nothing);
        Assert.That(async () => await acceptTask, Throws.Nothing));

        QuicMultiplexedConnection CreateClientConnection() =>
            (QuicMultiplexedConnection)provider.GetRequiredService<IMultiplexedClientTransport>().CreateConnection(
                provider.GetRequiredService<IListener<IMultiplexedConnection>>().ServerAddress,
                provider.GetRequiredService<IOptions<MultiplexedConnectionOptions>>().Value,
                provider.GetService<SslClientAuthenticationOptions>());
    }

    public class CertificateValidationCallback
    {
        public bool Result { get; set; } = false;
        public bool Validate() => Result;
    }
}
