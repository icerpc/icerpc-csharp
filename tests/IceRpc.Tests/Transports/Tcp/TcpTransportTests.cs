// Copyright (c) ZeroC, Inc.

using IceRpc.Transports;
using IceRpc.Transports.Tcp;
using IceRpc.Transports.Tcp.Internal;
using NUnit.Framework;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

namespace IceRpc.Tests.Transports.Tcp;

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
            ClientCertificates =
            [
                X509CertificateLoader.LoadPkcs12FromFile(
                    "client.p12",
                    password: null,
                    keyStorageFlags: X509KeyStorageFlags.Exportable)
            ],
            RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => true,
        };

    private static SslServerAuthenticationOptions DefaultSslServerAuthenticationOptions { get; } =
        new SslServerAuthenticationOptions
        {
            ClientCertificateRequired = false,
            ServerCertificate = X509CertificateLoader.LoadPkcs12FromFile(
                "server.p12",
                password: null,
                keyStorageFlags: X509KeyStorageFlags.Exportable),
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
            new TransportAddress { Host = "127.0.0.1" },
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
        using TcpClientConnection connection = CreateTcpClientConnection(
            new TransportAddress { Host = "127.0.0.1" },
            options: new TcpClientTransportOptions
            {
                LocalNetworkAddress = new IPEndPoint(IPAddress.Loopback, 10000),
            });

        Assert.That(connection.Socket.LocalEndPoint, Is.InstanceOf<IPEndPoint>());
        var localEndpoint = (IPEndPoint)connection.Socket.LocalEndPoint!;
        Assert.That(localEndpoint.Port, Is.EqualTo(10000));
        Assert.That(
            localEndpoint.Address,
            Is.EqualTo(localEndpoint.Address.IsIPv4MappedToIPv6 ?
                IPAddress.Parse("::ffff:127.0.0.1") : IPAddress.Loopback));
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

        using TcpClientConnection clientConnection = CreateTcpClientConnection(listener.TransportAddress);
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
        // The listen backlog does not work on macos 26.0, so we skip this test. Works on macos 26.1.
        if (OperatingSystem.IsMacOS() &&
            Environment.OSVersion.Version.Major == 26 &&
            Environment.OSVersion.Version.Minor == 0)
        {
            Assert.Ignore("Skipping listen backlog test on macOS 26.0 due to listen backlog bug.");
        }

        // Arrange
        const int backlog = 18;
        const int hardLimit = 50;

        await using IListener<IDuplexConnection> listener = CreateTcpListener(
            options: new TcpServerTransportOptions
            {
                ListenBacklog = backlog
            });

        IDuplexClientTransport clientTransport = new TcpClientTransport(new TcpClientTransportOptions());

        var connections = new List<IDuplexConnection>();

        // Act
        while (connections.Count < hardLimit)
        {
            using var source = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            try
            {
                IDuplexConnection clientConnection = clientTransport.CreateConnection(
                    listener.TransportAddress,
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
        Assert.That(connections, Has.Count.GreaterThanOrEqualTo(backlog));

        // The OS may allow a few more connections than specified by the ListenBacklog. This test ensures that
        // Socket.Listen was called with the ListenBacklog value set in TcpServerTransportOptions.
        Assert.That(connections, Has.Count.LessThan(hardLimit));

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
        var transportAddress = new TransportAddress { Host = "foo" };

        // Act/Assert
        Assert.Throws<ArgumentException>(() => CreateTcpListener(transportAddress));
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
            listener.TransportAddress,
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
    public async Task Tcp_transport_connection_information([Values] bool tls)
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        await using IListener<IDuplexConnection> listener = CreateTcpListener(
            authenticationOptions: tls ? DefaultSslServerAuthenticationOptions : null);

        using TcpClientConnection clientConnection = CreateTcpClientConnection(
            listener.TransportAddress,
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
            Is.EqualTo(AddressFamily.InterNetwork));
        var endPoint = (IPEndPoint?)transportConnectionInformation.LocalNetworkAddress;
        Assert.That(endPoint?.Address, Is.EqualTo(IPAddress.Loopback));
        Assert.That(transportConnectionInformation.RemoteNetworkAddress, Is.TypeOf<IPEndPoint>());
        endPoint = (IPEndPoint?)transportConnectionInformation.RemoteNetworkAddress;
        Assert.That(endPoint?.Address, Is.EqualTo(IPAddress.Loopback));
        Assert.That(
            transportConnectionInformation.RemoteCertificate,
            Is.EqualTo(tls ? DefaultSslServerAuthenticationOptions.ServerCertificate : null));
    }

    /// <summary>Verifies that a ssl client fails to connect to a tcp server.</summary>
    [Test]
    public async Task Ssl_client_fails_to_connect_to_tcp_server()
    {
        // Arrange
        await using IListener<IDuplexConnection> listener = CreateTcpListener();

        using TcpClientConnection clientConnection = CreateTcpClientConnection(
            listener.TransportAddress,
            authenticationOptions: DefaultSslClientAuthenticationOptions);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        // Act/Assert
        Assert.That(
            async () => await clientConnection.ConnectAsync(cts.Token),
            Throws.Exception.InstanceOf<OperationCanceledException>().Or.TypeOf<IceRpcException>());
    }

    [Test]
    public void Create_connection_to_valid_tcp_address() =>
        Assert.That(
            () =>
                new TcpClientTransport().CreateConnection(
                    new TransportAddress { Host = "127.0.0.1", Port = 0 },
                    new DuplexConnectionOptions(),
                    clientAuthenticationOptions: null).Dispose(),
           Throws.Nothing);

    [Test]
    public void Listen_on_valid_tcp_address() =>
        Assert.That(
            async () =>
                await new TcpServerTransport().Listen(
                    new TransportAddress { Host = "127.0.0.1", Port = 0 },
                    new DuplexConnectionOptions(),
                    serverAuthenticationOptions: DefaultSslServerAuthenticationOptions).DisposeAsync(),
           Throws.Nothing);

    [Test]
    public void Listen_on_ssl_address_without_ice_alpn_fails() =>
        Assert.That(
            () => new TcpServerTransport().Listen(
                new TransportAddress { Host = "127.0.0.1", Port = 0, TransportName = "ssl" },
                new DuplexConnectionOptions(),
                serverAuthenticationOptions: null),
            Throws.TypeOf<NotSupportedException>());

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
                listener.TransportAddress,
                authenticationOptions: DefaultSslClientAuthenticationOptions);

        Task<(IDuplexConnection Connection, EndPoint RemoteNetworkAddress)> acceptTask = listener.AcceptAsync(default);
        // We don't use clientConnection.ConnectAsync() here as this would start the TLS handshake
        await clientConnection.Socket.ConnectAsync(new DnsEndPoint(
            listener.TransportAddress.Host,
            listener.TransportAddress.Port));
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
                ServerCertificate = X509CertificateLoader.LoadPkcs12FromFile(
                    "server.p12",
                    password: null,
                    keyStorageFlags: X509KeyStorageFlags.Exportable),
                RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => false
            });

        using TcpClientConnection clientConnection = CreateTcpClientConnection(
            listener.TransportAddress,
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
        TransportAddress? transportAddress = null,
        TcpServerTransportOptions? options = null,
        SslServerAuthenticationOptions? authenticationOptions = null)
    {
        IDuplexServerTransport serverTransport = new TcpServerTransport(options ?? new());
        return serverTransport.Listen(
            transportAddress ?? new TransportAddress { Host = "127.0.0.1", Port = 0 },
            new DuplexConnectionOptions(),
            authenticationOptions);
    }

    private static TcpClientConnection CreateTcpClientConnection(
        TransportAddress transportAddress,
        TcpClientTransportOptions? options = null,
        SslClientAuthenticationOptions? authenticationOptions = null)
    {
        IDuplexClientTransport transport = new TcpClientTransport(options ?? new());
        return (TcpClientConnection)transport.CreateConnection(
            transportAddress,
            new DuplexConnectionOptions(),
            authenticationOptions);
    }
}
