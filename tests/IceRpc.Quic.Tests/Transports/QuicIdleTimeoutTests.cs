// Copyright (c) ZeroC, Inc.

using IceRpc.Tests.Common;
using IceRpc.Transports.Quic;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Buffers;
using System.IO.Pipelines;
using System.Net.Quic;

namespace IceRpc.Tests.Transports;

[NonParallelizable]
public class QuicIdleTimeoutTests
{
    [OneTimeSetUp]
    public void FixtureSetUp()
    {
        if (!QuicConnection.IsSupported)
        {
            Assert.Ignore("Quic is not supported on this platform");
        }
    }

    /// <summary>Verifies the QUIC connection is aborted by the idle timeout after remaining idle for more than idle
    /// timeout.</summary>
    /// <remarks>The behavior shown by this test is not desirable for IceRPC: we would prefer QUIC or IceRPC's QUIC
    /// wrapper to keep this connection alive when there is an outstanding request.
    /// See https://github.com/icerpc/icerpc-csharp/issues/3353.</remarks>
    [Test]
    public async Task Quic_connection_idle_after_idle_timeout([Values]bool configureServer)
    {
        // Arrange
        var services = new ServiceCollection().AddQuicTest();

        // The idle timeout is negotiated during connection establishment; as we result, we can set it on either (or
        // both) sides.
        if (configureServer)
        {
            services.AddOptions<QuicServerTransportOptions>("server").Configure(
                options => options.IdleTimeout = TimeSpan.FromMilliseconds(500));
        }
        else
        {
            services.AddOptions<QuicClientTransportOptions>("client").Configure(
                options => options.IdleTimeout = TimeSpan.FromMilliseconds(500));
        }

        await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

        var clientServerConnection = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await clientServerConnection.AcceptAndConnectAsync();
        using var sut = await clientServerConnection.CreateAndAcceptStreamAsync(bidirectional: true);

        // Simulate a request
        var data = new byte[] { 0x1, 0x2, 0x3 };
        await sut.Local.Output.WriteAsync(data);
        ReadResult readResult = await sut.Remote.Input.ReadAsync();

        // Act / Assert
        var startTime = TimeSpan.FromMilliseconds(Environment.TickCount64);

        Assert.That(readResult.Buffer.ToArray(), Is.EqualTo(data));

        Assert.That(
            async () => await sut.Local.Input.ReadAsync().AsTask(),
            Throws.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.ConnectionIdle));

        Assert.That(
            TimeSpan.FromMilliseconds(Environment.TickCount64) - startTime,
            Is.GreaterThan(TimeSpan.FromMilliseconds(490)));
    }
}
