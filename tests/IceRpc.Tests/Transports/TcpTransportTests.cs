// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

namespace IceRpc.Transports.Tests;

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

    /// <summary>Verifies that setting <see cref="TcpTransportOptions.ReceiveBufferSize"/> and
    /// <see cref="TcpTransportOptions.SendBufferSize"/> configures the respective socket properties.</summary>
    /// <param name="bufferSize">The buffer size to test with.</param>
    [TestCase(16 * 1024)]
    [TestCase(64 * 1024)]
    [TestCase(256 * 1024)]
    [TestCase(384 * 1024)]
    public void Configure_client_connection_buffer_size(int bufferSize)
    {
        // Act
        using TcpClientNetworkConnection connection = CreateTcpClientConnection(
            options: new TcpClientTransportOptions
            {
                ReceiveBufferSize = bufferSize,
                SendBufferSize = bufferSize,
            });

        // Assert
        Assert.Multiple(() =>
        {
            // The OS might allocate more space than the requested size.
            Assert.That(connection.Socket.SendBufferSize, Is.GreaterThanOrEqualTo(bufferSize));
            Assert.That(connection.Socket.ReceiveBufferSize, Is.GreaterThanOrEqualTo(bufferSize));
        });

        // But ensure it doesn't allocate too much as well
        if (OperatingSystem.IsLinux())
        {
            Assert.Multiple(() =>
            {
                // Linux allocates twice the size.
                Assert.That(connection.Socket.SendBufferSize, Is.LessThanOrEqualTo(2.5 * bufferSize));
                Assert.That(connection.Socket.ReceiveBufferSize, Is.LessThanOrEqualTo(2.5 * bufferSize));
            });
        }
        else
        {
            Assert.Multiple(() =>
            {
                // Windows typically allocates the requested size and macOS allocates a little more than the
                // requested size.
                Assert.That(connection.Socket.SendBufferSize, Is.LessThanOrEqualTo(1.5 * bufferSize));
                Assert.That(connection.Socket.ReceiveBufferSize, Is.LessThanOrEqualTo(1.5 * bufferSize));
            });
        }
    }

    /// <summary>Verifies that setting the <see cref="TcpClientTransportOptions.LocalEndPoint"/> properties, sets
    /// the socket local endpoint.</summary>
    [Test]
    public void Configure_client_connection_local_endpoint()
    {
        var localEndPoint = new IPEndPoint(IPAddress.IPv6Loopback, 10000);

        using TcpClientNetworkConnection connection = CreateTcpClientConnection(
            options: new TcpClientTransportOptions
            {
                LocalEndPoint = localEndPoint,
            });

        Assert.That(connection.Socket.LocalEndPoint, Is.EqualTo(localEndPoint));
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

        using TcpClientNetworkConnection clientConnection = CreateTcpClientConnection(listener.Endpoint);
        await clientConnection.ConnectAsync(default);

        // Act
        using var serverConnection = (TcpServerNetworkConnection)await acceptTask;

        // Assert
        Assert.Multiple(() =>
        {
            // The OS might allocate more space than the requested size.
            Assert.That(serverConnection.Socket.SendBufferSize, Is.GreaterThanOrEqualTo(bufferSize));
            Assert.That(serverConnection.Socket.ReceiveBufferSize, Is.GreaterThanOrEqualTo(bufferSize));
        });

        // But ensure it doesn't allocate too much as well
        if (OperatingSystem.IsMacOS())
        {
            Assert.Multiple(() =>
            {
                // macOS appears to have a low limit of a little more than 256KB for the receive buffer and
                // 64KB for the send buffer.
                Assert.That(serverConnection.Socket.SendBufferSize,
                            Is.LessThanOrEqualTo(1.5 * Math.Max(bufferSize, 64 * 1024)));
                Assert.That(serverConnection.Socket.ReceiveBufferSize,
                            Is.LessThanOrEqualTo(1.5 * Math.Max(bufferSize, 256 * 1024)));
            });
        }
        else if (OperatingSystem.IsLinux())
        {
            Assert.Multiple(() =>
            {
                // Linux allocates twice the size
                Assert.That(serverConnection.Socket.SendBufferSize, Is.LessThanOrEqualTo(2.5 * bufferSize));
                Assert.That(serverConnection.Socket.ReceiveBufferSize, Is.LessThanOrEqualTo(2.5 * bufferSize));
            });
        }
        else
        {
            Assert.Multiple(() =>
            {
                Assert.That(serverConnection.Socket.SendBufferSize, Is.LessThanOrEqualTo(1.5 * bufferSize));
                Assert.That(serverConnection.Socket.ReceiveBufferSize, Is.LessThanOrEqualTo(1.5 * bufferSize));
            });
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
        Assert.That(connections, Has.Count.GreaterThanOrEqualTo(18));
        Assert.That(connections, Has.Count.LessThanOrEqualTo(25));

        foreach (IDisposable connection in connections)
        {
            connection.Dispose();
        }
    }

    /// <summary>Verifies that connect cancellation works if connect hangs.</summary>
    [Test]
    public async Task Connect_cancellation()
    {
        // Arrange
        await using IListener<ISimpleNetworkConnection> listener = CreateTcpListener(
            options: new TcpServerTransportOptions
            {
                ListenerBackLog = 1
            });

        var clientTransport = new TcpClientTransport(new TcpClientTransportOptions());

        using var cancellationTokenSource = new CancellationTokenSource();
        Task<NetworkConnectionInformation> connectTask;
        TcpClientNetworkConnection clientConnection;
        while (true)
        {
            TcpClientNetworkConnection? connection = CreateTcpClientConnection(listener.Endpoint);
            try
            {
                connectTask = connection.ConnectAsync(cancellationTokenSource.Token);
                await Task.Delay(TimeSpan.FromMilliseconds(20));
                if (connectTask.IsCompleted)
                {
                    await connectTask;
                }
                else
                {
                    clientConnection = connection;
                    connection = null;
                    break;
                }
            }
            finally
            {
                if (connection != null)
                {
                    connection.Dispose();
                }
            }
        }

        // Act
        cancellationTokenSource.Cancel();

        // Assert
        Assert.That(async () => await connectTask, Throws.InstanceOf<OperationCanceledException>());
        clientConnection.Dispose();
    }

    /// <summary>Verifies that the client connect call on a tls connection fails with
    /// <see cref="OperationCanceledException"/> when the cancellation token is canceled.</summary>
    [Test]
    public async Task Tls_client_connect_operation_canceled_exception()
    {
        // Arrange
        using var cancellationSource = new CancellationTokenSource();
        await using IListener<ISimpleNetworkConnection> listener = CreateTcpListener(
            authenticationOptions: DefaultSslServerAuthenticationOptions);

        using TcpClientNetworkConnection clientConnection = CreateTcpClientConnection(
            listener.Endpoint,
            authenticationOptions:
                new SslClientAuthenticationOptions
                {
                    RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) =>
                    {
                        cancellationSource.Cancel();
                        Thread.Sleep(100);
                        return false;
                    }
                });

        Task<NetworkConnectionInformation> connectTask = clientConnection.ConnectAsync(cancellationSource.Token);
        ISimpleNetworkConnection serverConnection = await listener.AcceptAsync();
        _ = serverConnection.ConnectAsync(cancellationSource.Token);

        // Act/Assert
        Assert.That(async () => await connectTask, Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public async Task Tcp_network_connection_information([Values(true, false)] bool tls)
    {
        // Arrange
        using var cancellationSource = new CancellationTokenSource();
        await using IListener<ISimpleNetworkConnection> listener = CreateTcpListener(
            authenticationOptions: tls ? DefaultSslServerAuthenticationOptions : null);

        using TcpClientNetworkConnection clientConnection = CreateTcpClientConnection(
            listener.Endpoint,
            authenticationOptions: tls ? DefaultSslClientAuthenticationOptions : null);

        Task<NetworkConnectionInformation> connectTask = clientConnection.ConnectAsync(default);
        ISimpleNetworkConnection serverConnection = await listener.AcceptAsync();
        Task<NetworkConnectionInformation> serverConnectTask = serverConnection.ConnectAsync(cancellationSource.Token);

        // Act
        var networkConnectionInformation = await connectTask;

        // Assert
        Assert.That(networkConnectionInformation.IdleTimeout, Is.EqualTo(TimeSpan.FromSeconds(60)));
        Assert.That(networkConnectionInformation.LocalEndPoint, Is.TypeOf<IPEndPoint>());
        Assert.That(networkConnectionInformation.LocalEndPoint.AddressFamily, Is.EqualTo(AddressFamily.InterNetworkV6));
        var endPoint = (IPEndPoint)networkConnectionInformation.LocalEndPoint;
        Assert.That(endPoint.Address, Is.EqualTo(IPAddress.IPv6Loopback));
        Assert.That(networkConnectionInformation.RemoteEndPoint, Is.TypeOf<IPEndPoint>());
        endPoint = (IPEndPoint)networkConnectionInformation.RemoteEndPoint;
        Assert.That(endPoint.Address, Is.EqualTo(IPAddress.IPv6Loopback));
        Assert.That(
            networkConnectionInformation.RemoteCertificate,
            Is.EqualTo(tls ? DefaultSslServerAuthenticationOptions.ServerCertificate : null));
    }

    /// <summary>Verifies that the server connect call on a tls connection fails if the client previously disposed its
    /// connection. For tcp connections the server connect call is non-op.</summary>
    [Test]
    public async Task Tls_server_connection_connect_failed_exception()
    {
        // Arrange
        await using IListener<ISimpleNetworkConnection> listener =
            CreateTcpListener(authenticationOptions: DefaultSslServerAuthenticationOptions);
        using TcpClientNetworkConnection clientConnection =
            CreateTcpClientConnection(listener.Endpoint, authenticationOptions: DefaultSslClientAuthenticationOptions);

        Task<ISimpleNetworkConnection> acceptTask = listener.AcceptAsync();
        // We don't use clientConnection.ConnectAsync() here as this would start the TLS handshake
        await clientConnection.Socket.ConnectAsync(new DnsEndPoint(listener.Endpoint.Host, listener.Endpoint.Port));
        ISimpleNetworkConnection serverConnection = await acceptTask;
        clientConnection.Dispose();

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
        await using IListener<ISimpleNetworkConnection> listener = CreateTcpListener(
            authenticationOptions: new SslServerAuthenticationOptions
            {
                ServerCertificate = new X509Certificate2("../../../certs/server.p12", "password"),
                RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) =>
                {
                    cancellationSource.Cancel();
                    Thread.Sleep(100);
                    return false;
                }
            });

        using TcpClientNetworkConnection clientConnection = CreateTcpClientConnection(
            listener.Endpoint,
            authenticationOptions: DefaultSslClientAuthenticationOptions);

        Task<NetworkConnectionInformation> connectTask = clientConnection.ConnectAsync(default);
        ISimpleNetworkConnection serverConnection = await listener.AcceptAsync();
        Task<NetworkConnectionInformation> serverConnectTask = serverConnection.ConnectAsync(cancellationSource.Token);

        // Act/Assert
        Assert.That(async () => await serverConnectTask, Throws.InstanceOf<OperationCanceledException>());
    }

    private static IListener<ISimpleNetworkConnection> CreateTcpListener(
        Endpoint? endpoint = null,
        TcpServerTransportOptions? options = null,
        SslServerAuthenticationOptions? authenticationOptions = null)
    {
        IServerTransport<ISimpleNetworkConnection> serverTransport = new TcpServerTransport(options ?? new());
        return serverTransport.Listen(
            endpoint ?? new Endpoint(Protocol.IceRpc) { Host = "::1", Port = 0 },
            authenticationOptions: authenticationOptions,
            NullLogger.Instance);
    }

    private static TcpClientNetworkConnection CreateTcpClientConnection(
        Endpoint? endpoint = null,
        TcpClientTransportOptions? options = null,
        SslClientAuthenticationOptions? authenticationOptions = null)
    {
        IClientTransport<ISimpleNetworkConnection> transport = new TcpClientTransport(options ?? new());
        return (TcpClientNetworkConnection)transport.CreateConnection(
            endpoint ?? new Endpoint(Protocol.IceRpc),
            authenticationOptions: authenticationOptions,
            NullLogger.Instance);
    }
}
