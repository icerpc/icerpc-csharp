// Copyright (c) ZeroC, Inc.

using NUnit.Framework;
using IceRpc.Internal; // NotFoundDispatcher
using IceRpc.Tests.Common;
using System.Net.Quic;
using System.Security.Cryptography.X509Certificates;

namespace IceRpc.Tests;

[Parallelizable(ParallelScope.All)]
public class DefaultTransportTests
{
    /// <summary>Verifies we can connect successfully with the various transport names supported by the default
    /// multiplexed transports.</summary>
    [TestCase(null, null)]
    [TestCase("quic", "quic")]
    [TestCase("tcp", "tcp")]
    [TestCase(null, "quic")]
    [TestCase("quic", null)]
    public async Task Connect_with_default_multiplexed_transport(
        string? clientTransportName,
        string? serverTransportName)
    {
        if (!QuicConnection.IsSupported)
        {
            Assert.Ignore("QUIC is not supported on this platform");
        }

        // Arrange
        using X509Certificate2 serverCertificate = X509CertificateLoader.LoadPkcs12FromFile(
            "server.p12",
            password: null,
            keyStorageFlags: X509KeyStorageFlags.Exportable);

        var serverAddressUri = serverTransportName is null ?
            new Uri("icerpc://127.0.0.1:0") : new Uri($"icerpc://127.0.0.1:0?transport={serverTransportName}");

        await using var server = new Server(
            NotFoundDispatcher.Instance,
            serverAddressUri,
            serverAuthenticationOptions: serverCertificate.ToServerAuthenticationOptions());

        ServerAddress serverAddress = server.Listen(); // with the port resolved

        // Fix transport.
        serverAddress = serverAddress with { Transport = clientTransportName };

        using X509Certificate2 rootCA = X509CertificateLoader.LoadCertificateFromFile("cacert.der");
        await using var clientConnection = new ClientConnection(
            serverAddress,
            clientAuthenticationOptions: rootCA.ToClientAuthenticationOptions());

        // Act & Assert
        Assert.That(async () => await clientConnection.ConnectAsync(), Throws.Nothing);

        // Cleanup
        await clientConnection.ShutdownAsync();
        await server.ShutdownAsync();
    }

    /// <summary>Verifies we can connect successfully with the transport name(s) supported by the default
    /// duplex transports.</summary>
    [TestCase(null, null)]
    [TestCase("tcp", "tcp")]
    [TestCase(null, "tcp")]
    [TestCase("tcp", null)]
    public async Task Connect_with_default_duplex_transport(string? clientTransportName, string? serverTransportName)
    {
        // Arrange
        var serverAddressUri = serverTransportName is null ?
            new Uri("ice://127.0.0.1:0") : new Uri($"ice://127.0.0.1:0?transport={serverTransportName}");

        await using var server = new Server(NotFoundDispatcher.Instance, serverAddressUri);
        ServerAddress serverAddress = server.Listen(); // with the port resolved

        // Fix transport.
        serverAddress = serverAddress with { Transport = clientTransportName };

        await using var clientConnection = new ClientConnection(serverAddress);

        // Act & Assert
        Assert.That(async () => await clientConnection.ConnectAsync(), Throws.Nothing);

        // Cleanup
        await clientConnection.ShutdownAsync();
        await server.ShutdownAsync();
    }
}
