// Copyright (c) ZeroC, Inc.

using IceRpc.Transports;
using IceRpc.Transports.Internal;
using NUnit.Framework;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

namespace IceRpc.Tests.Transports;

[Parallelizable(scope: ParallelScope.All)]
public class TcpTransportTests
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Security",
        "CA5359:Do Not Disable Certificate Validation",
        Justification = "The transport tests do not rely on certificate validation")]
    private static SslClientAuthenticationOptions DefaultSslClientAuthenticationOptions { get; } =
        new SslClientAuthenticationOptions
        {
            ClientCertificates = new X509CertificateCollection()
            {
                new X509Certificate2("../../../certs/client.p12", "password")
            },
            RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => true,
        };

    private static SslServerAuthenticationOptions DefaultSslServerAuthenticationOptions { get; } =
        new SslServerAuthenticationOptions
        {
            ClientCertificateRequired = false,
            ServerCertificate = new X509Certificate2("../../../certs/server.p12", "password")
        };

    /// <summary>Verifies that setting <see cref="TcpTransportOptions.ReceiveBufferSize" /> and
    /// <see cref="TcpTransportOptions.SendBufferSize" /> configures the respective socket properties.</summary>
    /// <param name="bufferSize">The buffer size to test with.</param>
    [TestCase(16 * 1024)]
    [TestCase(64 * 1024)]
    [TestCase(256 * 1024)]
    [TestCase(384 * 1024)]
    public void Configure_client_connection_buffer_size(int bufferSize)
    {
        // Act
        using TcpClientConnection connection = CreateTcpClientConnection(
            new ServerAddress(Protocol.IceRpc),
            options: new TcpClientTransportOptions
            {
                ReceiveBufferSize = bufferSize,
                SendBufferSize = bufferSize,
            });

        // Assert
        // The OS might allocate more space than the requested size.
        Assert.That(connection.Socket.SendBufferSize, Is.GreaterThanOrEqualTo(bufferSize));
        Assert.That(connection.Socket.ReceiveBufferSize, Is.GreaterThanOrEqualTo(bufferSize));

        // But ensure it doesn't allocate too much as well
        if (OperatingSystem.IsLinux())
        {
            // Linux allocates twice the size.
            Assert.That(connection.Socket.SendBufferSize, Is.LessThanOrEqualTo(2.5 * bufferSize));
            Assert.That(connection.Socket.ReceiveBufferSize, Is.LessThanOrEqualTo(2.5 * bufferSize));
        }
        else
        {
            // Windows typically allocates the requested size and macOS allocates a little more than the
            // requested size.
            Assert.That(connection.Socket.SendBufferSize, Is.LessThanOrEqualTo(1.5 * bufferSize));
            Assert.That(connection.Socket.ReceiveBufferSize, Is.LessThanOrEqualTo(1.5 * bufferSize));
        }
    }

    /// <summary>Verifies that setting the <see cref="TcpClientTransportOptions.LocalNetworkAddress" /> properties, sets
    /// the socket local server address.</summary>
    [Test]
    public void Configure_client_connection_local_network_address()
    {
        var localNetworkAddress = new IPEndPoint(IPAddress.IPv6Loopback, 10000);

        using TcpClientConnection connection = CreateTcpClientConnection(
            new ServerAddress(Protocol.IceRpc),
            options: new TcpClientTransportOptions
            {
                LocalNetworkAddress = localNetworkAddress,
            });

        Assert.That(connection.Socket.LocalEndPoint, Is.EqualTo(localNetworkAddress));
    }

    /// <summary>Verifies that setting <see cref="TcpTransportOptions.ReceiveBufferSize" /> and
    /// <see cref="TcpTransportOptions.SendBufferSize" /> configures the respective socket properties.</summary>
    /// <param name="bufferSize">The buffer size to test with.</param>
    [TestCase(16 * 1024)]
    [TestCase(64 * 1024)]
    [TestCase(256 * 1024)]
    [TestCase(384 * 1024)]
    public async Task Configure_server_connection_buffer_size(int bufferSize)
    {
        // Arrange
        await using IListener<IDuplexConnection> listener = CreateTcpListener(
            options: new TcpServerTransportOptions
            {
                ReceiveBufferSize = bufferSize,
                SendBufferSize = bufferSize,
            });
        Task<(IDuplexConnection Connection, EndPoint RemoteNetworkAddress)> acceptTask = listener.AcceptAsync(default);

        IDuplexClientTransport clientTransport = new TcpClientTransport(
            new TcpClientTransportOptions());

        using TcpClientConnection clientConnection = CreateTcpClientConnection(listener.ServerAddress);
        await clientConnection.ConnectAsync(default);

        // Act
        using var serverConnection = (TcpServerConnection)(await acceptTask).Connection;

        // Assert
        // The OS might allocate more space than the requested size.
        Assert.That(serverConnection.Socket.SendBufferSize, Is.GreaterThanOrEqualTo(bufferSize));
        Assert.That(serverConnection.Socket.ReceiveBufferSize, Is.GreaterThanOrEqualTo(bufferSize));

        // But ensure it doesn't allocate too much as well
        if (OperatingSystem.IsMacOS())
        {
            // macOS appears to have a low limit of a little more than 256KB for the receive buffer and
            // 64KB for the send buffer.
            Assert.That(serverConnection.Socket.SendBufferSize,
                        Is.LessThanOrEqualTo(1.5 * Math.Max(bufferSize, 64 * 1024)));
            Assert.That(serverConnection.Socket.ReceiveBufferSize,
                        Is.LessThanOrEqualTo(1.5 * Math.Max(bufferSize, 256 * 1024)));
        }
        else if (OperatingSystem.IsLinux())
        {
            // Linux allocates twice the size
            Assert.That(serverConnection.Socket.SendBufferSize, Is.LessThanOrEqualTo(2.5 * bufferSize));
            Assert.That(serverConnection.Socket.ReceiveBufferSize, Is.LessThanOrEqualTo(2.5 * bufferSize));
        }
        else
        {
            Assert.That(serverConnection.Socket.SendBufferSize, Is.LessThanOrEqualTo(1.5 * bufferSize));
            Assert.That(serverConnection.Socket.ReceiveBufferSize, Is.LessThanOrEqualTo(1.5 * bufferSize));
        }
    }

    /// <summary>Verifies that setting the <see cref="TcpServerTransportOptions.ListenBacklog" /> configures the
    /// socket listen backlog.</summary>
    [Test]
    public async Task Configure_server_connection_listen_backlog()
    {
        // Arrange
        await using IListener<IDuplexConnection> listener = CreateTcpListener(
            options: new TcpServerTransportOptions
            {
                ListenBacklog = 18
            });

        IDuplexClientTransport clientTransport = new TcpClientTransport(new TcpClientTransportOptions());

        var connections = new List<IDuplexConnection>();

        // Act
        while (true)
        {
            using var source = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            try
            {
                IDuplexConnection clientConnection = clientTransport.CreateConnection(
                    listener.ServerAddress,
                    new DuplexConnectionOptions(),
                    null);
                await clientConnection.ConnectAsync(source.Token);
                connections.Add(clientConnection);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        // Assert
        Assert.That(connections, Has.Count.GreaterThanOrEqualTo(18));
        Assert.That(connections, Has.Count.LessThanOrEqualTo(25));

        foreach (IDisposable connection in connections)
        {
            connection.Dispose();
        }
    }

    /// <summary>Verifies that using a DNS name for a TCP listener server address fails with <see
    /// cref="NotSupportedException" /> exception.</summary>
    [Test]
    public void DNS_name_cannot_be_used_in_a_tcp_listener_server_address()
    {
        // Arrange
        var address = new ServerAddress(Protocol.IceRpc) { Host = "foo" };

        // Act/Assert
        Assert.Throws<ArgumentException>(() => CreateTcpListener(address));
    }

    /// <summary>Verifies that the client connect call on a tls connection fails with
    /// <see cref="OperationCanceledException" /> when the cancellation token is canceled.</summary>
    [Test]
    public async Task Tls_client_connect_operation_canceled_exception()
    {
        // Arrange

        using var cts = new CancellationTokenSource();
        await using IListener<IDuplexConnection> listener = CreateTcpListener(
            authenticationOptions: DefaultSslServerAuthenticationOptions);

        using TcpClientConnection clientConnection = CreateTcpClientConnection(
            listener.ServerAddress,
            authenticationOptions:
                new SslClientAuthenticationOptions
                {
                    RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => false
                });

        Task<TransportConnectionInformation> clientConnectTask = clientConnection.ConnectAsync(cts.Token);

        using IDuplexConnection serverConnection = (await listener.AcceptAsync(default)).Connection;
        cts.Cancel();
        var serverConnectTask = serverConnection.ConnectAsync(CancellationToken.None);

        // Act/Assert
        Assert.That(async () => await clientConnectTask, Throws.InstanceOf<OperationCanceledException>());
        clientConnection.Dispose();
        Assert.That(async () => await serverConnectTask, Throws.InstanceOf<IceRpcException>());
    }

    [Test]
    public async Task Tcp_transport_connection_information([Values(true, false)] bool tls)
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        await using IListener<IDuplexConnection> listener = CreateTcpListener(
            authenticationOptions: tls ? DefaultSslServerAuthenticationOptions : null);

        using TcpClientConnection clientConnection = CreateTcpClientConnection(
            listener.ServerAddress,
            authenticationOptions: tls ? DefaultSslClientAuthenticationOptions : null);

        Task<TransportConnectionInformation> connectTask = clientConnection.ConnectAsync(default);
        IDuplexConnection serverConnection = (await listener.AcceptAsync(default)).Connection;
        Task<TransportConnectionInformation> serverConnectTask = serverConnection.ConnectAsync(cts.Token);

        // Act
        var transportConnectionInformation = await connectTask;
        await serverConnectTask;

        // Assert
        Assert.That(transportConnectionInformation.LocalNetworkAddress, Is.TypeOf<IPEndPoint>());
        Assert.That(
            transportConnectionInformation.LocalNetworkAddress?.AddressFamily,
            Is.EqualTo(AddressFamily.InterNetworkV6));
        var endPoint = (IPEndPoint?)transportConnectionInformation.LocalNetworkAddress;
        Assert.That(endPoint?.Address, Is.EqualTo(IPAddress.IPv6Loopback));
        Assert.That(transportConnectionInformation.RemoteNetworkAddress, Is.TypeOf<IPEndPoint>());
        endPoint = (IPEndPoint?)transportConnectionInformation.RemoteNetworkAddress;
        Assert.That(endPoint?.Address, Is.EqualTo(IPAddress.IPv6Loopback));
        Assert.That(
            transportConnectionInformation.RemoteCertificate,
            Is.EqualTo(tls ? DefaultSslServerAuthenticationOptions.ServerCertificate : null));
    }

    /// <summary>Verifies that the server connect call on a tls connection fails if the client previously disposed its
    /// connection. For tcp connections the server connect call is non-op.</summary>
    [Test]
    public async Task Tls_server_connection_connect_fails_exception()
    {
        // Arrange
        await using IListener<IDuplexConnection> listener =
            CreateTcpListener(authenticationOptions: DefaultSslServerAuthenticationOptions);
        using TcpClientConnection clientConnection =
            CreateTcpClientConnection(
                listener.ServerAddress,
                authenticationOptions: DefaultSslClientAuthenticationOptions);

        Task<(IDuplexConnection Connection, EndPoint RemoteNetworkAddress)> acceptTask = listener.AcceptAsync(default);
        // We don't use clientConnection.ConnectAsync() here as this would start the TLS handshake
        await clientConnection.Socket.ConnectAsync(new DnsEndPoint(
            listener.ServerAddress.Host,
            listener.ServerAddress.Port));
        IDuplexConnection serverConnection = (await acceptTask).Connection;
        clientConnection.Dispose();

        // Act/Assert
        IceRpcException? exception = Assert.ThrowsAsync<IceRpcException>(
            async () => await serverConnection.ConnectAsync(default));
        Assert.That(
            exception!.IceRpcError,
            Is.EqualTo(IceRpcError.ConnectionAborted),
            $"The test failed with an unexpected IceRpcError {exception}");
    }

    /// <summary>Verifies that the server connect call on a tls connection fails with
    /// <see cref="OperationCanceledException" /> when the cancellation token is canceled.</summary>
    [Test]
    public async Task Tls_server_connect_operation_canceled_exception()
    {
        // Arrange
        await using IListener<IDuplexConnection> listener = CreateTcpListener(
            authenticationOptions: new SslServerAuthenticationOptions
            {
                ServerCertificate = new X509Certificate2("../../../certs/server.p12", "password"),
                RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => false
            });

        using TcpClientConnection clientConnection = CreateTcpClientConnection(
            listener.ServerAddress,
            authenticationOptions: DefaultSslClientAuthenticationOptions);

        Task<TransportConnectionInformation> clientConnectTask = clientConnection.ConnectAsync(default);
        IDuplexConnection serverConnection = (await listener.AcceptAsync(default)).Connection;
        Task<TransportConnectionInformation> serverConnectTask =
            serverConnection.ConnectAsync(new CancellationToken(canceled: true));

        // Act/Assert
        Assert.That(async () => await serverConnectTask, Throws.InstanceOf<OperationCanceledException>());
        serverConnection.Dispose();
        Assert.That(async () => await clientConnectTask, Throws.InstanceOf<IceRpcException>());
    }

    private static IListener<IDuplexConnection> CreateTcpListener(
        ServerAddress? serverAddress = null,
        TcpServerTransportOptions? options = null,
        SslServerAuthenticationOptions? authenticationOptions = null)
    {
        IDuplexServerTransport serverTransport = new TcpServerTransport(options ?? new());
        return serverTransport.Listen(
            serverAddress ?? new ServerAddress(Protocol.IceRpc) { Host = "::1", Port = 0 },
            new DuplexConnectionOptions(),
            authenticationOptions);
    }

    private static TcpClientConnection CreateTcpClientConnection(
        ServerAddress serverAddress,
        TcpClientTransportOptions? options = null,
        SslClientAuthenticationOptions? authenticationOptions = null)
    {
        IDuplexClientTransport transport = new TcpClientTransport(options ?? new());
        return (TcpClientConnection)transport.CreateConnection(
            serverAddress,
            new DuplexConnectionOptions(),
            authenticationOptions);
    }
}
