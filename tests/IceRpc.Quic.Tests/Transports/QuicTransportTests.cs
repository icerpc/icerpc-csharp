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
            new ClientConnectionOptions
            {
                ConnectTimeout = TimeSpan.FromSeconds(30),
                ServerAddress = server.ServerAddress,
                ClientAuthenticationOptions = blockingClientAuthenticationOptions
            },
            multiplexedClientTransport: clientTransport);

        await using var connection2 = new ClientConnection(
            new ClientConnectionOptions
            {
                ConnectTimeout = TimeSpan.FromSeconds(30),
                ServerAddress = server.ServerAddress,
                ClientAuthenticationOptions = clientAuthenticationOptions,
            },
            multiplexedClientTransport: clientTransport);


        await using var connection0 = new ClientConnection(
            new ClientConnectionOptions
            {
                ConnectTimeout = TimeSpan.FromSeconds(30),
                ServerAddress = server.ServerAddress,
                ClientAuthenticationOptions = clientAuthenticationOptions,
            },
            multiplexedClientTransport: clientTransport);

        await connection0.ConnectAsync();
        Console.WriteLine("connection0 connected");

        Task<TransportConnectionInformation> connect1Task = connection1.ConnectAsync();
        await tcs.Task;

        Console.WriteLine("establishing second connection");

        Task<TransportConnectionInformation> connect2Task = connection2.ConnectAsync();

        await Task.Delay(TimeSpan.FromSeconds(15));
        Console.WriteLine($"connect1Task is completed after 15s: {connect1Task.IsCompleted}");
        Console.WriteLine($"connect2Task is completed after 15s: {connect2Task.IsCompleted}");
        Console.WriteLine("releasing semaphore for connect1Task");
        semaphore.Release();
        await Task.Delay(TimeSpan.FromSeconds(5));
        Console.WriteLine($"connect1Task is completed after 20s: {connect1Task.IsCompleted}"); // should be true
        Console.WriteLine($"connect2Task is completed after 20s: {connect2Task.IsCompleted}"); // should be true, is often false

        Assert.That(async () => await connect1Task, Throws.InstanceOf<ConnectionException>());
        Assert.That(async () => await connect2Task, Throws.InstanceOf<TimeoutException>()); // unexpected
        // Assert.That(async () => await connect2Task, Throws.Nothing);

        await using var connection3 = new ClientConnection(
            new ClientConnectionOptions
            {
                ConnectTimeout = TimeSpan.FromSeconds(30),
                ServerAddress = server.ServerAddress,
                ClientAuthenticationOptions = clientAuthenticationOptions,
            },
            multiplexedClientTransport: clientTransport);

        await connection3.ConnectAsync();
        Console.WriteLine("done");
    }
}
