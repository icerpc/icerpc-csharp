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
    public async Task Accept_stream_failure()
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

        IListener<IMultiplexedConnection> listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();

        Task<TransportConnectionInformation> connectTask = connection1.ConnectAsync(default);
        using var cts = new CancellationTokenSource();
        Task<(IMultiplexedConnection Connection, EndPoint RemoteNetworkAddress)> acceptTask =
            listener.AcceptAsync(cts.Token);

        Assert.That(async () => await connectTask, Throws.TypeOf<TransportException>());
        clientValidationCallback.Result = true;
        connectTask = connection2.ConnectAsync(default);
        Assert.That(async () => await connectTask, Throws.TypeOf<TransportException>());
        cts.Cancel();
        Assert.That(async () => await acceptTask, Throws.TypeOf<OperationCanceledException>());

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
