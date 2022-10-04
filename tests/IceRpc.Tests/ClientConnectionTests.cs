// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal; // ServiceNotFoundDispatcher
using IceRpc.Tests.Common;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Tests;

[Parallelizable(ParallelScope.All)]
public class ClientConnectionTests
{
    private static List<Protocol> Protocols => new() { Protocol.IceRpc, Protocol.Ice };

    /// <summary>Verifies that <see cref="ClientConnection.ConnectAsync" /> returns a valid <see
    /// cref="TransportConnectionInformation" /></summary>
    [Test, TestCaseSource(nameof(Protocols))]
    public async Task Connect_returns_transport_connection_information(Protocol protocol)
    {
        // Arrange
        await using var server = new Server(
            new ServerOptions()
            {
                ConnectionOptions = new ConnectionOptions()
                {
                    Dispatcher = ServiceNotFoundDispatcher.Instance,
                },
                ServerAddress = new ServerAddress(new Uri($"{protocol}://127.0.0.1:0"))
            },
            multiplexedServerTransport: new SlicServerTransport(new TcpServerTransport()),
            duplexServerTransport: new TcpServerTransport());
        server.Listen();

        await using var connection = new ClientConnection(
            new ClientConnectionOptions() { ServerAddress = server.ServerAddress },
            duplexClientTransport: new TcpClientTransport(),
            multiplexedClientTransport: new SlicClientTransport(new TcpClientTransport()));

        // Act
        TransportConnectionInformation transportConnectionInformation = await connection.ConnectAsync();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(transportConnectionInformation.LocalNetworkAddress, Is.Not.Null);
            Assert.That(transportConnectionInformation.RemoteNetworkAddress, Is.Not.Null);
        });
    }

    [Test]
    public async Task Connection_can_reconnect_after_underlying_connection_shutdown()
    {
        // Arrange
        var server = new Server(ServiceNotFoundDispatcher.Instance, new Uri("icerpc://127.0.0.1:0"));
        server.Listen();
        ServerAddress serverAddress = server.ServerAddress;
        await using var connection = new ClientConnection(serverAddress);
        await connection.ConnectAsync();
        await server.DisposeAsync();
        server = new Server(ServiceNotFoundDispatcher.Instance, serverAddress);
        server.Listen();

        // Act/Assert
        Assert.That(async () => await connection.ConnectAsync(), Throws.Nothing);

        await server.DisposeAsync();
    }

    [Test]
    public async Task Connection_can_reconnect_after_peer_abort()
    {
        // Arrange
        var server = new Server(ServiceNotFoundDispatcher.Instance, new Uri("icerpc://127.0.0.1:0"));
        server.Listen();
        ServerAddress serverAddress = server.ServerAddress;
        await using var connection = new ClientConnection(serverAddress);
        await connection.ConnectAsync();

        try
        {
            // Cancel shutdown and dispose to abort the connection.
            await server.ShutdownAsync(new CancellationToken(true));
        }
        catch
        {
        }
        await server.DisposeAsync();

        server = new Server(ServiceNotFoundDispatcher.Instance, serverAddress);
        server.Listen();

        // Act/Assert
        Assert.That(async () => await connection.ConnectAsync(), Throws.Nothing);

        await server.DisposeAsync();
    }

    [Test, TestCaseSource(nameof(Protocols))]
    public async Task Connection_invoke_reconnect_after_underlying_connection_shutdown(Protocol protocol)
    {
        // Arrange
        var server = new Server(ServiceNotFoundDispatcher.Instance, new Uri($"{protocol.Name}://127.0.0.1:0"));
        server.Listen();
        ServerAddress serverAddress = server.ServerAddress;
        await using var connection = new ClientConnection(serverAddress);
        await connection.ConnectAsync();
        await server.DisposeAsync();
        server = new Server(ServiceNotFoundDispatcher.Instance, serverAddress);
        server.Listen();

        var request = new OutgoingRequest(new ServiceAddress(protocol));

        // Act/Assert
        Assert.That(async () => await connection.InvokeAsync(request), Throws.Nothing);

        await server.DisposeAsync();
    }

    /// <summary>Verifies that ClientConnection.ServerAddress.Transport property is set.</summary>
    [Test, TestCaseSource(nameof(Protocols))]
    public async Task Connection_server_address_transport_property_is_set(Protocol protocol)
    {
        // Arrange
        await using var clientConnection = new ClientConnection(new ServerAddress(protocol));

        // Act/Assert
        Assert.That(clientConnection.ServerAddress.Transport, Is.Not.Null);
    }

    /// <summary>Verifies that InvokeAsync succeeds when there is a compatible server address.</summary>
    [TestCase("icerpc://testhost.com?transport=coloc")]
    [TestCase("icerpc://testhost.com:4062")]
    [TestCase("icerpc://testhost.com")]
    [TestCase("icerpc://foo.com/path?alt-server=testhost.com")]
    [TestCase("icerpc:/path")]
    [TestCase("ice://testhost.com:4061/path")]
    public async Task InvokeAsync_succeeds_with_a_compatible_server_address(ServiceAddress serviceAddress)
    {
        // Arrange
        await using ServiceProvider provider =
            new ServiceCollection()
                .AddColocTest(
                    new InlineDispatcher((request, cancellationToken) => new(new OutgoingResponse(request))),
                    serviceAddress.Protocol!,
                    host: "testhost.com")
                .BuildServiceProvider(validateScopes: true);

        Server server = provider.GetRequiredService<Server>();
        ClientConnection connection = provider.GetRequiredService<ClientConnection>();

        server.Listen();

        // Assert
        Assert.That(
            async () => await connection.InvokeAsync(new OutgoingRequest(serviceAddress), default),
            Throws.Nothing);
    }

    /// <summary>Verifies that InvokeAsync fails when there is no compatible server address.</summary>
    [TestCase("icerpc://foo.com?transport=tcp", "icerpc://foo.com?transport=coloc")]
    [TestCase("icerpc://foo.com", "icerpc://foo.com?transport=coloc")]
    [TestCase("icerpc://foo.com", "icerpc://bar.com")]
    [TestCase("icerpc://foo.com", "icerpc://foo.com:10000")]
    [TestCase("icerpc://foo.com", "icerpc://foo.com?tanpot=tcp")]
    [TestCase("icerpc://foo.com", "icerpc://foo.com?t=10000")]
    [TestCase("ice://foo.com?t=10000&z", "ice://foo.com:10000/path?t=10000&z")]
    public async Task InvokeAsync_fails_without_a_compatible_server_address(
        ServerAddress serverAddress,
        ServiceAddress serviceAddress)
    {
        // Arrange
        await using var connection = new ClientConnection(serverAddress);

        // Assert
        Assert.That(
            async () => await connection.InvokeAsync(new OutgoingRequest(serviceAddress)),
            Throws.TypeOf<InvalidOperationException>());
    }

    /// <summary>Verifies that InvokeAsync fails when the protocols don't match.</summary>
    [TestCase("icerpc://foo.com", "ice:/path")]
    [TestCase("ice://foo.com", "icerpc:/path")]
    public async Task InvokeAsync_fails_with_protocol_mismatch(
        ServerAddress serverAddress,
        ServiceAddress serviceAddress)
    {
        // Arrange
        await using ServiceProvider provider =
            new ServiceCollection()
                .AddColocTest(
                    new InlineDispatcher((request, cancellationToken) => new(new OutgoingResponse(request))),
                    serverAddress.Protocol,
                    host: serverAddress.Host)
                .BuildServiceProvider(validateScopes: true);

        Server server = provider.GetRequiredService<Server>();
        ClientConnection connection = provider.GetRequiredService<ClientConnection>();

        server.Listen();

        // Assert
        Assert.That(
            async () => await connection.InvokeAsync(new OutgoingRequest(serviceAddress)),
            Throws.TypeOf<InvalidOperationException>());
    }
}
