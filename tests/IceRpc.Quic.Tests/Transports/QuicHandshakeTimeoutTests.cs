// Copyright (c) ZeroC, Inc.

using IceRpc.Tests.Common;
using IceRpc.Transports;
using IceRpc.Transports.Quic;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Buffers;
using System.Net.Quic;

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

    /// <summary>Verifies that the default HandshakeTimeout value (10 seconds) allows a connection to be established
    /// successfully.</summary>
    [Test]
    public async Task Quic_connection_succeeds_with_default_handshake_timeout()
    {
        // Arrange
        var services = new ServiceCollection().AddQuicTest();

        // Use the default MultiplexedConnectionOptions (HandshakeTimeout = 10 seconds)
        await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

        var clientServerConnection = provider.GetRequiredService<ClientServerMultiplexedConnection>();

        // Act/Assert
        // The connection should succeed with the default handshake timeout
        await clientServerConnection.AcceptAndConnectAsync();
    }

    /// <summary>Verifies that the connection can send and receive data after connecting with a custom handshake
    /// timeout.</summary>
    [Test]
    public async Task Quic_connection_works_after_connecting_with_custom_handshake_timeout()
    {
        // Arrange
        var services = new ServiceCollection().AddQuicTest();

        // Configure a custom handshake timeout
        services.AddOptions<MultiplexedConnectionOptions>().Configure(
            options => options.HandshakeTimeout = TimeSpan.FromSeconds(15));

        await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

        var clientServerConnection = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await clientServerConnection.AcceptAndConnectAsync();

        // Create a stream and verify data can be sent/received
        using var sut = await clientServerConnection.CreateAndAcceptStreamAsync(bidirectional: true);

        // Act
        var data = new byte[] { 0x1, 0x2, 0x3 };
        await sut.Local.Output.WriteAsync(data);
        var readResult = await sut.Remote.Input.ReadAsync();

        // Assert
        Assert.That(readResult.Buffer.ToArray(), Is.EqualTo(data));
    }
}
