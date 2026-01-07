// Copyright (c) ZeroC, Inc.

using IceRpc.Transports;
using IceRpc.Transports.Quic.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using System.Diagnostics;
using System.Net.Quic;
using System.Net.Security;
using System.Security.Authentication;

namespace IceRpc.Tests.Transports;

[NonParallelizable]
public class QuicTransportTests
{
    [OneTimeSetUp]
    public void FixtureSetUp()
    {
        if (!QuicConnection.IsSupported)
        {
            Assert.Ignore("QUIC is not supported on this platform");
        }
        Trace.Listeners.Add(new ConsoleTraceListener());
    }

    [Test]
    public async Task Listener_accept_fails_after_client_certificate_validation_callback_rejects_the_connection()
    {
        // Arrange
        int connectionNum = 0;
        IServiceCollection services = new ServiceCollection().AddQuicTest();
        services.AddSingleton(
            new SslClientAuthenticationOptions
            {
                // First connection is rejected, the following connections are accepted.
                RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => ++connectionNum > 1,
            });
        await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

        QuicMultiplexedConnection connection = CreateClientConnection();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();

        // Act
        var acceptTask = listener.AcceptAsync(default);

        // Assert
        Assert.That(async () => await connection.ConnectAsync(default), Throws.TypeOf<AuthenticationException>());
        Assert.That(async () => await acceptTask, Throws.TypeOf<AuthenticationException>());

        QuicMultiplexedConnection CreateClientConnection() =>
            (QuicMultiplexedConnection)provider.GetRequiredService<IMultiplexedClientTransport>().CreateConnection(
                provider.GetRequiredService<IListener<IMultiplexedConnection>>().ServerAddress,
                provider.GetRequiredService<IOptions<MultiplexedConnectionOptions>>().Value,
                provider.GetRequiredService<SslClientAuthenticationOptions>());
    }
}
