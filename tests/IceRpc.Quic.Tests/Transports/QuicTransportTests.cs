// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slice;
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
    public async Task Client_certificate_validation_callback_blocks_accept_loop()
    {
        // Arrange
        var tcs = new TaskCompletionSource();
        using var semaphore = new Semaphore(initialCount: 0, maximumCount: 1);
        IServiceCollection services = new ServiceCollection().AddQuicTest();
        var sslServerAuthenticationOptions = new SslServerAuthenticationOptions
        {
            ServerCertificate = new X509Certificate2("../../../certs/server.p12", "password"),
        };
        var blockingClientAuthenticationOptions = new SslClientAuthenticationOptions
        {
            RemoteCertificateValidationCallback = (sender, certificate, chain, errors) =>
            {
                tcs.SetResult();
                semaphore.WaitOne();
                return true;
            },
        };

        var clientAuthenticationOptions = new SslClientAuthenticationOptions
        {
            RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => true,
        };

        var dispatcher = new InlineDispatcher((request, cancellationToken) => new(new OutgoingResponse(request)));
        var serverTransport = new QuicServerTransport();
        await using var server = new Server(
            dispatcher,
            new ServerAddress(new Uri("icerpc://127.0.0.1:0")),
            sslServerAuthenticationOptions,
            multiplexedServerTransport: serverTransport);
        server.Listen();

        var clientTransport = new QuicClientTransport();
        await using var connection1 = new ClientConnection(
            server.ServerAddress,
            blockingClientAuthenticationOptions,
            multiplexedClientTransport: clientTransport);

        await using var connection2 = new ClientConnection(
            server.ServerAddress,
            clientAuthenticationOptions,
            multiplexedClientTransport: clientTransport);

        var serviceProxy1 = new ServiceProxy(connection1);
        var serviceProxy2 = new ServiceProxy(connection2);

        _ = serviceProxy1.IcePingAsync();
        await tcs.Task;
        // Act/Assert
        Assert.That(async () => await serviceProxy2.IcePingAsync(), Throws.Nothing);
        semaphore.Release();
    }
}
