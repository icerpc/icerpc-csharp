// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace IceRpc.Transports.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class TcpTransportTests
{
    /// <summary>Verifies that the transport can accept TCP network connections.</summary>
    [Test]
    public async Task Accept_tcp_network_connection()
    {
        // Arrange
        await using IListener<ISimpleNetworkConnection> listener = CreateTcpListener();
        await using TcpClientNetworkConnection clientConnection =
            CreateTcpClientConnection(listener.Endpoint with { Host = "localhost" });

        Task<ISimpleNetworkConnection> acceptTask = listener.AcceptAsync();
        await clientConnection.ConnectAsync(default);

        // Act/Assert
        Assert.That(
            async () =>
            {
                await using ISimpleNetworkConnection _ = await acceptTask;
            },
            Throws.Nothing);
    }

    /// <summary>Verifies that calling connect on a tcp client connection with a canceled cancellation token fails with
    /// <see cref="OperationCanceledException"/>.</summary>
    [Test]
    public async Task Client_connect_with_canceled_cancellation_token()
    {
        // Arrange
        await using IListener<ISimpleNetworkConnection> listener = CreateTcpListener();
        await using TcpClientNetworkConnection clientConnection = CreateTcpClientConnection(listener.Endpoint);
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        // Act/Assert
        Assert.That(async () => await clientConnection.ConnectAsync(cancellationSource.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }

    /// <summary>Verifies that setting <see cref="TcpTransportOptions.ReceiveBufferSize"/> and
    /// <see cref="TcpTransportOptions.SendBufferSize"/> configures the respective socket properties.</summary>
    /// <param name="bufferSize">The buffer size to test with.</param>
    [TestCase(16 * 1024)]
    [TestCase(64 * 1024)]
    [TestCase(256 * 1024)]
    [TestCase(384 * 1024)]
    public void Client_connection_buffer_size(int bufferSize)
    {
        // Arrange
        IClientTransport<ISimpleNetworkConnection> clientTransport = new TcpClientTransport(
            new TcpClientTransportOptions
            {
                ReceiveBufferSize = bufferSize,
                SendBufferSize = bufferSize,
            });

        // Act
        var connection = (TcpClientNetworkConnection)clientTransport.CreateConnection(
            new Endpoint(Protocol.IceRpc),
            authenticationOptions: null,
            NullLogger.Instance);

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

    /// <summary>Verifies that a dual mode socket is created when <see cref="TcpTransportOptions.IsIPv6Only"/> is set
    /// to <c>true</c>.</summary>
    /// <param name="ipv6only">The value for <see cref="TcpTransportOptions.IsIPv6Only"/>.</param>
    /// <returns></returns>
    [Test]
    public async Task Client_connection_is_ipv6_only([Values(true, false)] bool ipv6only)
    {
        IClientTransport<ISimpleNetworkConnection> clientTransport = new TcpClientTransport(
            new TcpClientTransportOptions
            {
                IsIPv6Only = ipv6only
            });

        await using var connection = (TcpClientNetworkConnection)clientTransport.CreateConnection(
            new Endpoint(Protocol.IceRpc) { Host = "::1" },
            authenticationOptions: null,
            NullLogger.Instance);

        Assert.That(connection.Socket.DualMode, ipv6only ? Is.False : Is.True);
    }


    /// <summary>Verifies that setting the <see cref="TcpClientTransportOptions.LocalEndPoint"/> properties, sets
    /// the socket local endpoint.</summary>
    [Test]
    public async Task Client_connection_local_endpoint()
    {
        var localEndpoint = new IPEndPoint(IPAddress.IPv6Loopback, 10000);

        await using TcpClientNetworkConnection connection =  CreateTcpClientConnection(
            new Endpoint(Protocol.IceRpc) { Host = "::1" },
            new TcpClientTransportOptions
            {
                LocalEndPoint = localEndpoint,
            });

        Assert.That(connection.Socket.LocalEndPoint, Is.EqualTo(localEndpoint));
    }

    /// <summary>Verifies that a server connection created with <see cref="TcpTransportOptions.IsIPv6Only"/> set to
    /// false creates a dual mode socket, and accepts connections from IPv4 mapped addresses.</summary>
    [Test]
    public async Task Dual_mode_server_connection_accepts_ipv4_mapped_addresses()
    {
        // Arrange
        IServerTransport<ISimpleNetworkConnection> serverTransport = new TcpServerTransport(
            new TcpServerTransportOptions
            {
                IsIPv6Only = false
            });

        IListener<ISimpleNetworkConnection> listener = serverTransport.Listen(
            new Endpoint(Protocol.IceRpc) { Host = "::0", Port = 0 },
            authenticationOptions: null,
            NullLogger.Instance);
        Task<ISimpleNetworkConnection> acceptTask = listener.AcceptAsync();

        IClientTransport<ISimpleNetworkConnection> clientTransport =
            new TcpClientTransport(new TcpClientTransportOptions());

        await using var clientConnection = (TcpClientNetworkConnection)clientTransport.CreateConnection(
            listener.Endpoint with { Host = "::FFFF:127.0.0.1" },
            authenticationOptions: null,
            NullLogger.Instance);

        // Act/Assert
        Assert.That(() => clientConnection.ConnectAsync(default), Throws.Nothing);
    }

    /// <summary>Verifies that a server connection created with <see cref="TcpTransportOptions.IsIPv6Only"/> set to
    /// true does not create a dual mode socket, and does not accept connections from IPv4 mapped addresses.</summary>
    [Test]
    public async Task IPv6_only_server_connection_does_not_accept_ipv4_mapped_addresses()
    {
        // Arrange
        IServerTransport<ISimpleNetworkConnection> serverTransport = new TcpServerTransport(
            new TcpServerTransportOptions
            {
                IsIPv6Only = true
            });

        IListener<ISimpleNetworkConnection> listener = serverTransport.Listen(
            new Endpoint(Protocol.IceRpc) { Host = "::0", Port = 0 },
            authenticationOptions: null,
            NullLogger.Instance);
        Task<ISimpleNetworkConnection> acceptTask = listener.AcceptAsync();

        IClientTransport<ISimpleNetworkConnection> clientTransport =
            new TcpClientTransport(new TcpClientTransportOptions());

        await using var clientConnection = (TcpClientNetworkConnection)clientTransport.CreateConnection(
            listener.Endpoint with { Host = "::FFFF:127.0.0.1" },
            authenticationOptions: null,
            NullLogger.Instance);

        // Act/Assert
        Assert.That(() => clientConnection.ConnectAsync(default),
            Throws.TypeOf<ConnectionRefusedException>());
    }

    /// <summary>Verifies that calling listen twice fails with a <see cref="TransportException"/>.</summary>
    [Test]
    public async Task Listen_twice_on_the_same_address_fails_with_a_transport_exception()
    {
        // Arrange
        IServerTransport<ISimpleNetworkConnection> serverTransport = new TcpServerTransport();
        await using IListener<ISimpleNetworkConnection> listener = serverTransport.Listen(
            new Endpoint(Protocol.IceRpc) { Host = "::0", Port = 0 },
            authenticationOptions: null,
            NullLogger.Instance);

        // Act/Assert
        Assert.That(
            () => serverTransport.Listen(listener.Endpoint, authenticationOptions: null, NullLogger.Instance),
            Throws.TypeOf<TransportException>());
    }

    /// <summary>Verifies that reading from a disposed tcp client connection returns zero.</summary>
    [Test]
    public async Task Read_from_disposed_client_connection_returns_zero()
    {
        // Arrange
        await using IListener<ISimpleNetworkConnection> listener = CreateTcpListener();
        await using TcpClientNetworkConnection clientConnection = CreateTcpClientConnection(listener.Endpoint);
        Task<ISimpleNetworkConnection> acceptTask = listener.AcceptAsync();
        await clientConnection.ConnectAsync(default);
        ISimpleNetworkConnection serverConnection = await acceptTask;
        await clientConnection.DisposeAsync();

        // Act
        int read = await serverConnection.ReadAsync(new byte[1], default);

        // Assert
        Assert.That(read, Is.Zero);
    }

    /// <summary>Verifies that reading from a disposed tcp server connection returns zero.</summary>
    [Test]
    public async Task Read_from_disposed_server_connection_returns_zero()
    {
        // Arrange
        await using IListener<ISimpleNetworkConnection> listener = CreateTcpListener();
        await using TcpClientNetworkConnection clientConnection =
            CreateTcpClientConnection(listener.Endpoint with { Host = "localhost" });

        Task<ISimpleNetworkConnection> acceptTask = listener.AcceptAsync();
        await clientConnection.ConnectAsync(default);
        ISimpleNetworkConnection serverConnection = await acceptTask;
        await serverConnection.DisposeAsync();

        // Act
        int read = await clientConnection.ReadAsync(new byte[1], default);

        // Assert
        Assert.That(read, Is.Zero);
    }

    /// <summary>Verifies that setting <see cref="TcpTransportOptions.ReceiveBufferSize"/> and
    /// <see cref="TcpTransportOptions.SendBufferSize"/> configures the respective socket properties.</summary>
    /// <param name="bufferSize">The buffer size to test with.</param>
    [TestCase(16 * 1024)]
    [TestCase(64 * 1024)]
    [TestCase(256 * 1024)]
    [TestCase(384 * 1024)]
    public async Task Server_connection_buffer_size(int bufferSize)
    {
        // Arrange
        IServerTransport<ISimpleNetworkConnection> serverTransport = new TcpServerTransport(
            new TcpServerTransportOptions
            {
                ReceiveBufferSize = bufferSize,
                SendBufferSize = bufferSize,
            });

        IListener<ISimpleNetworkConnection> listener = serverTransport.Listen(
            new Endpoint(Protocol.IceRpc) { Host = "::1", Port = 0 },
            authenticationOptions: null,
            NullLogger.Instance);
        Task<ISimpleNetworkConnection> acceptTask = listener.AcceptAsync();

        IClientTransport<ISimpleNetworkConnection> clientTransport = new TcpClientTransport(
            new TcpClientTransportOptions());

        await using var clientConnection = (TcpClientNetworkConnection)clientTransport.CreateConnection(
            listener.Endpoint,
            authenticationOptions: null,
            NullLogger.Instance);
        await clientConnection.ConnectAsync(default);

        // Act
        await using var serverConnection = (TcpServerNetworkConnection)await acceptTask;

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

    /// <summary>Verifies that setting the <see cref="TcpServerTransportOptions.ListenerBackLog"/> configures the
    /// socket listen backlog.</summary>
    [Test]
    public async Task Server_connection_listen_backlog()
    {
        // Arrange
        IServerTransport<ISimpleNetworkConnection> serverTransport = new TcpServerTransport(
            new TcpServerTransportOptions
            {
                ListenerBackLog = 18
            });

        IListener<ISimpleNetworkConnection> listener = serverTransport.Listen(
            new Endpoint(Protocol.IceRpc) { Host = "::1", Port = 0 },
            authenticationOptions: null,
            NullLogger.Instance);

        IClientTransport<ISimpleNetworkConnection> clientTransport =
            new TcpClientTransport(new TcpClientTransportOptions());

        var connections = new List<ISimpleNetworkConnection>();

        // Act
        while (true)
        {
            using var source = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
            try
            {
                ISimpleNetworkConnection clientConnection = clientTransport.CreateConnection(
                    listener.Endpoint,
                    authenticationOptions: null,
                    NullLogger.Instance);
                await clientConnection.ConnectAsync(source.Token);
                connections.Add(clientConnection);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        // Assert
        Assert.That(connections.Count, Is.GreaterThanOrEqualTo(18));
        Assert.That(connections.Count, Is.LessThanOrEqualTo(25));

        await Task.WhenAll(connections.Select(connection => connection.DisposeAsync().AsTask()));
    }

    /// <summary>Verifies that the client connect call on a tls connection fails with
    /// <see cref="OperationCanceledException"/> when the cancellation token is canceled.</summary>
    [Test]
    public async Task Tls_client_connect_operation_canceled_exception()
    {
        // Arrange
        await using IListener<ISimpleNetworkConnection> listener = CreteTcpListenerWithAuthOptions();

        await using TcpClientNetworkConnection clientConnection =
            CreateTcpClientConnectionWithAuthOptions(listener.Endpoint);

        using var cancellationSource = new CancellationTokenSource();
        Task<NetworkConnectionInformation> connectTask = clientConnection.ConnectAsync(cancellationSource.Token);
        cancellationSource.Cancel();

        // Act/Assert
        Assert.That(async () => await connectTask, Throws.InstanceOf<OperationCanceledException>());
    }

    /// <summary>Verifies that the server connect call on a tls connection fails if the client previously disposed its
    /// connection. For tcp connection the server connect call is non-op.</summary>
    [Test]
    public async Task Tls_server_connection_connect_failed_exception()
    {
        // Arrange
        await using IListener<ISimpleNetworkConnection> listener = CreteTcpListenerWithAuthOptions();
        await using TcpClientNetworkConnection clientConnection =
            CreateTcpClientConnectionWithAuthOptions(listener.Endpoint);

        Task<ISimpleNetworkConnection> acceptTask = listener.AcceptAsync();
        // We don't use clientConnection.ConnectAsync() here as this would start the TLS handshake
        await clientConnection.Socket.ConnectAsync(new DnsEndPoint(listener.Endpoint.Host, listener.Endpoint.Port));
        ISimpleNetworkConnection serverConnection = await acceptTask;
        await clientConnection.DisposeAsync();

        // Act/Assert
        Assert.That(
            async () => await serverConnection.ConnectAsync(default),
            Throws.TypeOf<ConnectFailedException>());
    }

    /// <summary>Verifies that the server connect call on a tls connection fails with
    /// <see cref="OperationCanceledException"/> when the cancellation token is canceled.</summary>
    [Test]
    public async Task Tls_server_connect_operation_canceled_exception()
    {
        // Arrange
        await using IListener<ISimpleNetworkConnection> listener = CreteTcpListenerWithAuthOptions();

        await using TcpClientNetworkConnection clientConnection =
            CreateTcpClientConnectionWithAuthOptions(listener.Endpoint);

        Task<ISimpleNetworkConnection> acceptTask = listener.AcceptAsync();
        // We don't use clientConnection.ConnectAsync() here as this would start the TLS handshake
        await clientConnection.Socket.ConnectAsync(new DnsEndPoint(listener.Endpoint.Host, listener.Endpoint.Port));
        ISimpleNetworkConnection serverConnection = await acceptTask;
        await clientConnection.DisposeAsync();
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        // Act/Assert
        Assert.That(
            async () => await serverConnection.ConnectAsync(cancellationSource.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }

    private static IListener<ISimpleNetworkConnection> CreteTcpListenerWithAuthOptions(
        TcpServerTransportOptions? options = null) => CreateTcpListener(
        options,
        authenticationOptions: new SslServerAuthenticationOptions
        {
            ClientCertificateRequired = false,
            ServerCertificate = new X509Certificate2("../../../certs/server.p12", "password")
        });

    private static IListener<ISimpleNetworkConnection> CreateTcpListener(
        TcpServerTransportOptions? options = null,
        SslServerAuthenticationOptions? authenticationOptions = null)
    {
        IServerTransport<ISimpleNetworkConnection> serverTransport = new TcpServerTransport(options ?? new());
        return serverTransport.Listen(
            new Endpoint(Protocol.IceRpc) { Host = "::1", Port = 0 },
            authenticationOptions: authenticationOptions,
            NullLogger.Instance);
    }

    private static TcpClientNetworkConnection CreateTcpClientConnectionWithAuthOptions(Endpoint endpoint) =>
        CreateTcpClientConnection(
            endpoint,
            authenticationOptions: new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = CertificateValidaton.GetServerCertificateValidationCallback(
                        certificateAuthorities: new X509Certificate2Collection()
                        {
                            new X509Certificate2("../../../certs/cacert.pem")
                        })
            });

    private static TcpClientNetworkConnection CreateTcpClientConnection(
        Endpoint endpoint,
        TcpClientTransportOptions? options = null,
        SslClientAuthenticationOptions? authenticationOptions = null)
    {
        IClientTransport<ISimpleNetworkConnection> transport = new TcpClientTransport(options ?? new());
        return (TcpClientNetworkConnection)transport.CreateConnection(
            endpoint,
            authenticationOptions: authenticationOptions,
            NullLogger.Instance);
    }
}
