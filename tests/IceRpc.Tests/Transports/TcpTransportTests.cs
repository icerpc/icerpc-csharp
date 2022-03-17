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
[Timeout(5000)]
public class TcpTransportTests
{
    /// <summary>Verifies that the transport can accept tcp network connections.</summary>
    [Test]
    public async Task Accept_tcp_network_connection()
    {
        // Arrange
        await using IListener<ISimpleNetworkConnection> listener = CreateTcpListener();
        await using TcpClientNetworkConnection clientConnection =
            CreateTcpClientConnection(listener.Endpoint);

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
    public async Task Calling_connect_with_canceled_cancellation_token_fails()
    {
        // Arrange
        await using TcpClientNetworkConnection clientConnection = CreateTcpClientConnection();

        // Act/Assert
        Assert.That(async () => await clientConnection.ConnectAsync(new CancellationToken(canceled: true)),
            Throws.InstanceOf<OperationCanceledException>());
    }

    /// <summary>Verifies that calling read on a client connection returns zero when the peer connection is disposed.
    /// </summary>
    [Test]
    public async Task Client_connection_read_from_disposed_peer_connection_returns_zero()
    {
        // Arrange
        await using IListener<ISimpleNetworkConnection> listener = CreateTcpListener();
        await using TcpClientNetworkConnection clientConnection =
            CreateTcpClientConnection(listener.Endpoint);

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
    public async Task Configure_client_connection_buffer_size(int bufferSize)
    {
        // Act
        await using TcpClientNetworkConnection connection = CreateTcpClientConnection(
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

    /// <summary>Verifies that a dual mode socket is created when <see cref="TcpTransportOptions.IsIPv6Only"/> is set
    /// to <c>true</c>.</summary>
    /// <param name="ipv6only">The value for <see cref="TcpTransportOptions.IsIPv6Only"/>.</param>
    /// <returns></returns>
    [Test]
    public async Task Configure_client_connection_is_ipv6_only([Values(true, false)] bool ipv6only)
    {
        await using TcpClientNetworkConnection? connection = CreateTcpClientConnection(
            new Endpoint(Protocol.IceRpc) { Host = "::1" },
            new TcpClientTransportOptions
            {
                IsIPv6Only = ipv6only
            });

        Assert.That(connection.Socket.DualMode, ipv6only ? Is.False : Is.True);
    }


    /// <summary>Verifies that setting the <see cref="TcpClientTransportOptions.LocalEndPoint"/> properties, sets
    /// the socket local endpoint.</summary>
    [Test]
    public async Task Configure_client_connection_local_endpoint()
    {
        var localEndpoint = new IPEndPoint(IPAddress.IPv6Loopback, 10000);

        await using TcpClientNetworkConnection connection =  CreateTcpClientConnection(
            options: new TcpClientTransportOptions
            {
                LocalEndPoint = localEndpoint,
            });

        Assert.That(connection.Socket.LocalEndPoint, Is.EqualTo(localEndpoint));
    }

    /// <summary>Verifies that setting <see cref="TcpTransportOptions.ReceiveBufferSize"/> and
    /// <see cref="TcpTransportOptions.SendBufferSize"/> configures the respective socket properties.</summary>
    /// <param name="bufferSize">The buffer size to test with.</param>
    [TestCase(16 * 1024)]
    [TestCase(64 * 1024)]
    [TestCase(256 * 1024)]
    [TestCase(384 * 1024)]
    public async Task Configure_server_connection_buffer_size(int bufferSize)
    {
        // Arrange
        await using IListener<ISimpleNetworkConnection> listener = CreateTcpListener(
            options: new TcpServerTransportOptions
            {
                ReceiveBufferSize = bufferSize,
                SendBufferSize = bufferSize,
            });
        Task<ISimpleNetworkConnection> acceptTask = listener.AcceptAsync();

        IClientTransport<ISimpleNetworkConnection> clientTransport = new TcpClientTransport(
            new TcpClientTransportOptions());

        await using TcpClientNetworkConnection clientConnection = CreateTcpClientConnection(listener.Endpoint);
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
    public async Task Configure_server_connection_listen_backlog()
    {
        // Arrange
        await using IListener<ISimpleNetworkConnection> listener = CreateTcpListener(
            options: new TcpServerTransportOptions
            {
                ListenerBackLog = 18
            });

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

    /// <summary>Verifies that we can write using server and client connections.</summary>
    [Test]
    public async Task Connection_write(
        [Values(1, 1024, 16 * 1024, 512 * 1023)] int size,
        [Values(true, false)] bool useServerConnection)
    {
        // Arrange
        await using IListener<ISimpleNetworkConnection> listener = CreateTcpListener();
        await using TcpClientNetworkConnection clientConnection = CreateTcpClientConnection(listener.Endpoint);

        Task<ISimpleNetworkConnection> acceptTask = listener.AcceptAsync();
        await clientConnection.ConnectAsync(default);
        var serverConnection = (TcpServerNetworkConnection)await acceptTask;
        byte[] writeBuffer = new byte[size];

        TcpNetworkConnection writeConnection = useServerConnection ? serverConnection : clientConnection;
        TcpNetworkConnection readConnection = useServerConnection ? clientConnection : serverConnection;

        // Act
        await writeConnection.WriteAsync(new ReadOnlyMemory<byte>[] { writeBuffer }, default);

        // Assert
        Memory<byte> readBuffer = new byte[size];
        int offset = 0;
        while (offset < size)
        {
            offset += await readConnection.ReadAsync(readBuffer[offset..], default);
        }
        Assert.That(offset, Is.EqualTo(size));
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

    /// <summary>Verifies that a read operation ends with <see cref="OperationCanceledException"/> if the given
    /// cancellation token is canceled.</summary>
    [Test]
    public async Task Read_cancellation()
    {
        // Arrange
        using var canceled = new CancellationTokenSource();
        await using IListener<ISimpleNetworkConnection> listener = CreateTcpListener();
        await using TcpClientNetworkConnection clientConnection = CreateTcpClientConnection(listener.Endpoint);

        Task<ISimpleNetworkConnection> acceptTask = listener.AcceptAsync();
        await clientConnection.ConnectAsync(default);
        ISimpleNetworkConnection serverConnection = await acceptTask;
        ValueTask<int> readTask = clientConnection.ReadAsync(new byte[1], canceled.Token);

        // Act
        canceled.Cancel();

        // Assert
        Assert.That(async () => await readTask, Throws.TypeOf<OperationCanceledException>());
    }

    /// <summary>Verifies that calling read on a disposed tcp client connection fails with
    /// <see cref="ObjectDisposedException"/>.</summary>
    [Test]
    public async Task Read_from_disposed_client_connection_fails()
    {
        // Arrange
        await using TcpClientNetworkConnection clientConnection = CreateTcpClientConnection();
        await clientConnection.DisposeAsync();

        // Act/Assert
        Assert.That(
            async () => await clientConnection.ReadAsync(new byte[1], default),
            Throws.TypeOf<ObjectDisposedException>());
    }

    /// <summary>Verifies that reading from the connection updates its last activity property.</summary>
    [Test]
    public async Task Read_updates_last_activity()
    {
        // Arrange
        await using IListener<ISimpleNetworkConnection> listener = CreateTcpListener();
        await using TcpClientNetworkConnection clientConnection = CreateTcpClientConnection(listener.Endpoint);

        Task<ISimpleNetworkConnection> acceptTask = listener.AcceptAsync();
        await clientConnection.ConnectAsync(default);
        ISimpleNetworkConnection serverConnection = await acceptTask;
        await serverConnection.WriteAsync(new ReadOnlyMemory<byte>[] { new byte[1] }, default);
        var delay = TimeSpan.FromMilliseconds(2);
        TimeSpan lastActivity = clientConnection.LastActivity;
        await Task.Delay(delay);
        var buffer = new Memory<byte>(new byte[1]);

        // Act
        await clientConnection.ReadAsync(buffer, default);

        // Assert
        Assert.That(clientConnection.LastActivity, Is.GreaterThanOrEqualTo(lastActivity + delay));
    }

    /// <summary>Verifies that calling read on a server connection returns zero when the peer connection is disposed.
    /// </summary>
    [Test]
    public async Task Server_connection_read_from_disposed_client_connection_returns_zero()
    {
        await using IListener<ISimpleNetworkConnection> listener = CreateTcpListener();
        await using TcpClientNetworkConnection clientConnection = CreateTcpClientConnection(listener.Endpoint);
        Task<ISimpleNetworkConnection> acceptTask = listener.AcceptAsync();
        await clientConnection.ConnectAsync(default);
        ISimpleNetworkConnection serverConnection = await acceptTask;
        await clientConnection.DisposeAsync();

        int read = await serverConnection.ReadAsync(new byte[1], default);

        Assert.That(read, Is.Zero);
    }

    /// <summary>Verifies that a server connection created with <see cref="TcpTransportOptions.IsIPv6Only"/> set to
    /// false creates a dual mode socket, and accepts connections from IPv4 mapped addresses.</summary>
    [Test]
    public async Task Server_connection_with_dual_mode_socket_accepts_incoming_connections_from_ipv4_mapped_addresses()
    {
        // Arrange
        await using IListener<ISimpleNetworkConnection> listener = CreateTcpListener(
            endpoint: new Endpoint(Protocol.IceRpc) { Host = "::0", Port = 0 },
            options: new TcpServerTransportOptions
            {
                IsIPv6Only = false
            });
        Task<ISimpleNetworkConnection> acceptTask = listener.AcceptAsync();

        await using TcpClientNetworkConnection clientConnection =
            CreateTcpClientConnection(listener.Endpoint with { Host = "::FFFF:127.0.0.1" });

        // Act/Assert
        Assert.That(() => clientConnection.ConnectAsync(default), Throws.Nothing);
    }

    /// <summary>Verifies that a server connection created with <see cref="TcpTransportOptions.IsIPv6Only"/> set to
    /// true does not create a dual mode socket, and does not accept connections from IPv4 mapped addresses.</summary>
    [Test]
    public async Task Server_connection_with_non_dual_mode_socket_does_not_accept_incoming_connections_from_ipv4_mapped_addresses()
    {
        // Arrange
        await using IListener<ISimpleNetworkConnection> listener = CreateTcpListener(
            options: new TcpServerTransportOptions
            {
                IsIPv6Only = true
            });
        Task<ISimpleNetworkConnection> acceptTask = listener.AcceptAsync();

        await using TcpClientNetworkConnection clientConnection =
            CreateTcpClientConnection(listener.Endpoint with { Host = "::FFFF:127.0.0.1" });

        // Act/Assert
        Assert.That(() => clientConnection.ConnectAsync(default),
            Throws.TypeOf<ConnectionRefusedException>());
    }

    /// <summary>Verifies that the client connect call on a tls connection fails with
    /// <see cref="OperationCanceledException"/> when the cancellation token is canceled.</summary>
    [Test]
    public async Task Tls_client_connect_operation_canceled_exception()
    {
        // Arrange
        using var cancellationSource = new CancellationTokenSource();
        using var semaphore = new SemaphoreSlim(0);
        await using IListener<ISimpleNetworkConnection> listener = CreateTcpListener(
            authenticationOptions: new SslServerAuthenticationOptions
            {
                ClientCertificateRequired = true,
                ServerCertificate = new X509Certificate2("../../../certs/server.p12", "password")
            });

        await using TcpClientNetworkConnection clientConnection = CreateTcpClientConnection(
            listener.Endpoint,
            authenticationOptions:
                new SslClientAuthenticationOptions
                {
                    ClientCertificates = new X509CertificateCollection()
                    {
                        new X509Certificate2("../../../certs/client.p12", "password")
                    },
                    RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) =>
                    {
                        cancellationSource.Cancel();
                        semaphore.Wait();
                        return false;
                    }
                });

        Task<NetworkConnectionInformation> connectTask = clientConnection.ConnectAsync(cancellationSource.Token);
        ISimpleNetworkConnection serverConnection =  await listener.AcceptAsync();
        _ = serverConnection.ConnectAsync(cancellationSource.Token);

        // Act/Assert
        Assert.That(async () => await connectTask, Throws.InstanceOf<OperationCanceledException>());
    }

    /// <summary>Verifies that the server connect call on a tls connection fails if the client previously disposed its
    /// connection. For tcp connections the server connect call is non-op.</summary>
    [Test]
    public async Task Tls_server_connection_connect_failed_exception()
    {
        // Arrange
        await using IListener<ISimpleNetworkConnection> listener = CreateTcpListener(withAuthenticationOptions: true);
        await using TcpClientNetworkConnection clientConnection =
            CreateTcpClientConnection(listener.Endpoint, withAuthenticationOptions: true);

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
        using var cancellationSource = new CancellationTokenSource();
        using var semaphore = new SemaphoreSlim(0);
        await using IListener<ISimpleNetworkConnection> listener = CreateTcpListener(
            authenticationOptions: new SslServerAuthenticationOptions
            {
                ServerCertificate = new X509Certificate2("../../../certs/server.p12", "password"),
                RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) =>
                {
                    cancellationSource.Cancel();
                    semaphore.Wait();
                    return false;
                }
            });

        await using TcpClientNetworkConnection clientConnection = CreateTcpClientConnection(
            listener.Endpoint,
            withAuthenticationOptions: true);

        Task<NetworkConnectionInformation> connectTask = clientConnection.ConnectAsync(cancellationSource.Token);
        ISimpleNetworkConnection serverConnection = await listener.AcceptAsync();
        var serverConnectTask = serverConnection.ConnectAsync(cancellationSource.Token);

        // Act/Assert
        Assert.That(async () => await serverConnectTask, Throws.InstanceOf<OperationCanceledException>());
    }

    /// <summary>Verifies that pending write operation fails with <see cref="OperationCanceledException"/> once the
    /// cancellation token is canceled.</summary>
    [Test]
    public async Task Write_cancellation()
    {
        // Arrange
        await using IListener<ISimpleNetworkConnection> listener = CreateTcpListener(
            options: new TcpServerTransportOptions()
            {
                ReceiveBufferSize = 4096,
            });
        await using TcpClientNetworkConnection clientConnection = CreateTcpClientConnection(
            listener.Endpoint,
            new TcpClientTransportOptions
            {
                SendBufferSize = 4096,
            });

        Task<ISimpleNetworkConnection> acceptTask = listener.AcceptAsync();
        await clientConnection.ConnectAsync(default);
        ISimpleNetworkConnection serverConnection = await acceptTask;
        var buffer = new List<ReadOnlyMemory<byte>>() { new byte[1024 * 1024] };
        using var canceled = new CancellationTokenSource();
        // Write completes as soon as the data is copied to the socket buffer, the test relies on the calls
        // not completing synchronously to be able to cancel them.
        Task writeTask;
        do
        {
            writeTask = clientConnection.WriteAsync(buffer, canceled.Token).AsTask();
            await Task.WhenAny(Task.Delay(TimeSpan.FromMilliseconds(100)), writeTask);
        }
        while (writeTask.IsCompleted);

        // Act
        canceled.Cancel();

        // Assert
        Assert.That(async () => await writeTask, Throws.TypeOf<OperationCanceledException>());
    }

    /// <summary>Verifies that calling write fails with <see cref="ConnectionLostException"/> when the peer connection
    /// is disposed.</summary>
    [Test]
    public async Task Write_connection_lost_exception()
    {
        // Arrange
        await using IListener<ISimpleNetworkConnection> listener = CreateTcpListener();
        await using TcpClientNetworkConnection clientConnection = CreateTcpClientConnection(listener.Endpoint);

        Task<ISimpleNetworkConnection> acceptTask = listener.AcceptAsync();
        await clientConnection.ConnectAsync(default);
        ISimpleNetworkConnection serverConnection = await acceptTask;
        await serverConnection.DisposeAsync();
        var buffer = new List<ReadOnlyMemory<byte>>() { new byte[1024 * 1024] };
        // The write task will complete successfully until we have fill the tcp buffer.
        Task writeTask;
        do
        {
            writeTask = clientConnection.WriteAsync(buffer, default).AsTask();
            await Task.WhenAny(Task.Delay(TimeSpan.FromMilliseconds(100)), writeTask);
        }
        while (writeTask.IsCompletedSuccessfully);

        // Act/Assert
        Assert.That(async () => await writeTask, Throws.InstanceOf<ConnectionLostException>());
    }

    /// <summary>Verifies that reading from a disposed tcp server connection returns zero.</summary>
    [Test]
    public async Task Write_to_disposed_client_connection_fails()
    {
        // Arrange
        await using TcpClientNetworkConnection clientConnection = CreateTcpClientConnection();
        await clientConnection.DisposeAsync();
        var buffer = new List<ReadOnlyMemory<byte>>() { new byte[1] { 0xFF } };

        // Act/Assert
        Assert.That(
            async () => await clientConnection.WriteAsync(buffer, default),
            Throws.TypeOf<ObjectDisposedException>());
    }

    /// <summary>Verifies that writing to the connection updates its last activity property.</summary>
    [Test]
    public async Task Write_updates_last_activity()
    {
        // Arrange
        await using IListener<ISimpleNetworkConnection> listener = CreateTcpListener();
        await using TcpClientNetworkConnection clientConnection = CreateTcpClientConnection(listener.Endpoint);

        Task<ISimpleNetworkConnection> acceptTask = listener.AcceptAsync();
        await clientConnection.ConnectAsync(default);
        ISimpleNetworkConnection serverConnection = await acceptTask;
        var delay = TimeSpan.FromMilliseconds(2);
        TimeSpan lastActivity = clientConnection.LastActivity;
        await Task.Delay(delay);

        // Act
        await clientConnection.WriteAsync(new ReadOnlyMemory<byte>[] { new byte[1] }, default);

        // Assert
        Assert.That(clientConnection.LastActivity, Is.GreaterThanOrEqualTo(lastActivity + delay));
    }

    /// <summary>Verifies that calling write with a canceled cancellation token fails with
    /// <see cref="OperationCanceledException"/>.</summary>
    [Test]
    public async Task Write_with_canceled_cancellation_token()
    {
        // Arrange
        await using IListener<ISimpleNetworkConnection> listener = CreateTcpListener();
        await using TcpClientNetworkConnection clientConnection = CreateTcpClientConnection(listener.Endpoint);

        Task<ISimpleNetworkConnection> acceptTask = listener.AcceptAsync();
        await clientConnection.ConnectAsync(default);
        ISimpleNetworkConnection serverConnection = await acceptTask;
        var buffer = new List<ReadOnlyMemory<byte>>() { new byte[1] { 0xFF } };

        // Act/Assert
        Assert.That(
            async () => await clientConnection.WriteAsync(buffer, new CancellationToken(canceled: true)),
            Throws.InstanceOf<OperationCanceledException>());
    }

    private static IListener<ISimpleNetworkConnection> CreateTcpListener(
        Endpoint? endpoint = null,
        TcpServerTransportOptions? options = null,
        SslServerAuthenticationOptions? authenticationOptions = null,
        bool withAuthenticationOptions = false)
    {
        IServerTransport<ISimpleNetworkConnection> serverTransport = new TcpServerTransport(options ?? new());
        return serverTransport.Listen(
            endpoint ?? new Endpoint(Protocol.IceRpc) { Host = "::1", Port = 0 },
            authenticationOptions: withAuthenticationOptions ?
                new SslServerAuthenticationOptions
                {
                    ClientCertificateRequired = false,
                    ServerCertificate = new X509Certificate2("../../../certs/server.p12", "password")
                } : authenticationOptions,
            NullLogger.Instance);
    }

    private static TcpClientNetworkConnection CreateTcpClientConnection(
        Endpoint? endpoint = null,
        TcpClientTransportOptions? options = null,
        SslClientAuthenticationOptions? authenticationOptions= null,
        bool withAuthenticationOptions = false)
    {
        IClientTransport<ISimpleNetworkConnection> transport = new TcpClientTransport(options ?? new());
        return (TcpClientNetworkConnection)transport.CreateConnection(
            endpoint ?? new Endpoint(Protocol.IceRpc),
            authenticationOptions: withAuthenticationOptions ?
                new SslClientAuthenticationOptions
                {
                    ClientCertificates = new X509CertificateCollection()
                    {
                        new X509Certificate2("../../../certs/client.p12", "password")
                    },
                    RemoteCertificateValidationCallback = CertificateValidaton.GetServerCertificateValidationCallback(
                        certificateAuthorities: new X509Certificate2Collection()
                        {
                            new X509Certificate2("../../../certs/cacert.pem")
                        })
                } : authenticationOptions,
            NullLogger.Instance);
    }
}
