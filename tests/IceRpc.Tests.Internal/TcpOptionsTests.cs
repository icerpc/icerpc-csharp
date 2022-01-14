// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using System.Net;
using System.Net.Sockets;

namespace IceRpc.Tests.Internal
{
    [Parallelizable(scope: ParallelScope.All)]
    [TestFixture(AddressFamily.InterNetwork)]
    [TestFixture(AddressFamily.InterNetworkV6)]
    [Timeout(5000)]
    public class TcpOptionsTests
    {
        private readonly bool _isIPv6;

        public TcpOptionsTests(AddressFamily addressFamily) => _isIPv6 = addressFamily == AddressFamily.InterNetworkV6;

        [TestCase(16 * 1024)]
        [TestCase(64 * 1024)]
        [TestCase(256 * 1024)]
        [TestCase(384 * 1024)]
        public async Task TcpOptions_Client_BufferSize(int size)
        {
            await using IListener<ISimpleNetworkConnection> listener = CreateListener();

            await using TcpClientNetworkConnection clientConnection = CreateClientConnection(
                listener.Endpoint,
                new TcpClientOptions
                {
                    SendBufferSize = size,
                    ReceiveBufferSize = size
                });

            Socket socket = clientConnection.Socket;

            // The OS might allocate more space than the requested size.
            Assert.That(socket.SendBufferSize, Is.GreaterThanOrEqualTo(size));
            Assert.That(socket.ReceiveBufferSize, Is.GreaterThanOrEqualTo(size));

            // But ensure it doesn't allocate too much as well
            if (OperatingSystem.IsLinux())
            {
                // Linux allocates twice the size.
                Assert.That(socket.SendBufferSize, Is.LessThanOrEqualTo(2.5 * size));
                Assert.That(socket.ReceiveBufferSize, Is.LessThanOrEqualTo(2.5 * size));
            }
            else
            {
                // Windows typically allocates the requested size and macOS allocates a little more than the
                // requested size.
                Assert.That(socket.SendBufferSize, Is.LessThanOrEqualTo(1.5 * size));
                Assert.That(socket.ReceiveBufferSize, Is.LessThanOrEqualTo(1.5 * size));
            }
        }

        [Test]
        public async Task TcpOptions_Client_IsIPv6Only()
        {
            await using IListener<ISimpleNetworkConnection> listener = CreateListener();

            await using TcpClientNetworkConnection clientConnection = CreateClientConnection(
                listener.Endpoint,
                new TcpClientOptions
                {
                    IsIPv6Only = true
                });

            Socket socket = clientConnection.Socket;

            Assert.That(socket.DualMode, Is.False);
        }

        [Test]
        public async Task TcpOptions_Client_LocalEndPoint()
        {
            int port = 45678;
            while (true)
            {
                try
                {
                    await using IListener<ISimpleNetworkConnection> listener = CreateListener();
                    var localEndPoint = new IPEndPoint(_isIPv6 ? IPAddress.IPv6Loopback : IPAddress.Loopback, port++);

                    await using TcpClientNetworkConnection clientConnection = CreateClientConnection(
                        listener.Endpoint,
                        new TcpClientOptions
                        {
                            LocalEndPoint = localEndPoint
                        });

                    Socket socket = clientConnection.Socket;

                    Assert.AreEqual(localEndPoint, socket.LocalEndPoint);
                    break;
                }
                catch (TransportException)
                {
                    // Retry with another port
                }
            }
        }

        [TestCase(16 * 1024)]
        [TestCase(64 * 1024)]
        [TestCase(256 * 1024)]
        [TestCase(384 * 1024)]
        public async Task TcpOptions_Server_BufferSizeAsync(int size)
        {
            await using IListener<ISimpleNetworkConnection> listener = CreateListener(tcpOptions: new TcpServerOptions
            {
                SendBufferSize = size,
                ReceiveBufferSize = size
            });

            Task<TcpServerNetworkConnection> acceptTask = CreateServerConnection(listener);
            await using TcpClientNetworkConnection clientConnection = CreateClientConnection(listener.Endpoint);

            _ = (clientConnection as ISimpleNetworkConnection).ConnectAsync(default);
            await using TcpServerNetworkConnection serverConnection = await acceptTask;

            Socket socket = serverConnection.Socket;

            // The OS might allocate more space than the requested size.
            Assert.That(socket.SendBufferSize, Is.GreaterThanOrEqualTo(size));
            Assert.That(socket.ReceiveBufferSize, Is.GreaterThanOrEqualTo(size));

            // But ensure it doesn't allocate too much as well
            if (OperatingSystem.IsMacOS())
            {
                // macOS Big Sur appears to have a low limit of a little more than 256KB for the receive buffer and
                // 64KB for the send buffer.
                Assert.That(socket.SendBufferSize,
                            Is.LessThanOrEqualTo(1.5 * Math.Max(size, 64 * 1024)));
                Assert.That(socket.ReceiveBufferSize,
                            Is.LessThanOrEqualTo(1.5 * Math.Max(size, 256 * 1024)));
            }
            else if (OperatingSystem.IsLinux())
            {
                // Linux allocates twice the size
                Assert.That(socket.SendBufferSize, Is.LessThanOrEqualTo(2.5 * size));
                Assert.That(socket.ReceiveBufferSize, Is.LessThanOrEqualTo(2.5 * size));
            }
            else
            {
                Assert.That(socket.SendBufferSize, Is.LessThanOrEqualTo(1.5 * size));
                Assert.That(socket.ReceiveBufferSize, Is.LessThanOrEqualTo(1.5 * size));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task TcpOptions_Server_IsIPv6OnlyAsync(bool ipv6Only)
        {
            if (_isIPv6)
            {
                // Create a server endpoint for ::0 instead of loopback

                await using IListener<ISimpleNetworkConnection> listener =
                    CreateListener(host: "[::0]",
                                         new TcpServerOptions() { IsIPv6Only = ipv6Only });

                Task<TcpServerNetworkConnection> acceptTask = CreateServerConnection(listener);

                // Create a client endpoints that uses the 127.0.0.1 IPv4-mapped address
                Endpoint clientEndpoint = listener.Endpoint with { Host = "::FFFF:127.0.0.1" };

                await using TcpClientNetworkConnection clientConnection = CreateClientConnection(clientEndpoint);

                var connectTask = (clientConnection as ISimpleNetworkConnection).ConnectAsync(default);

                if (ipv6Only)
                {
                    // This should fail, the server only accepts IPv6 connections
                    Assert.CatchAsync<ConnectFailedException>(async () => await connectTask);
                }
                else
                {
                    await using TcpServerNetworkConnection serverConnection = await acceptTask;
                    _ = await (serverConnection as ISimpleNetworkConnection).ConnectAsync(default);

                    // This should succeed, the server accepts IPv4 and IPv6 connections
                    Assert.DoesNotThrowAsync(async () => await connectTask);
                }
            }
        }

        [Test]
        public async Task TcpOptions_Server_ListenerBackLog()
        {
            await using IListener<ISimpleNetworkConnection> listener = CreateListener(tcpOptions: new TcpServerOptions
            {
                ListenerBackLog = 18
            });

            Task<TcpServerNetworkConnection> acceptTask = CreateServerConnection(listener);

            var connections = new List<TcpClientNetworkConnection>();
            while (true)
            {
                using var source = new CancellationTokenSource(500);

                TcpClientNetworkConnection clientConnection = CreateClientConnection(listener.Endpoint);

                try
                {
                    _ = await (clientConnection as ISimpleNetworkConnection).ConnectAsync(source.Token);
                    connections.Add(clientConnection);
                }
                catch (OperationCanceledException)
                {
                    await clientConnection.DisposeAsync();
                    break;
                }
            }

            // Tolerate a little more connections than the exact expected count (on Linux, it appears to accept one
            // more connection for instance).
            Assert.That(connections.Count, Is.GreaterThanOrEqualTo(19));
            Assert.That(connections.Count, Is.LessThanOrEqualTo(25));

            await Task.WhenAll(connections.Select(connection => connection.DisposeAsync().AsTask()));
        }

        private static TcpClientNetworkConnection CreateClientConnection(
            Endpoint endpoint,
            TcpClientOptions? tcpOptions = null)
        {
            IClientTransport<ISimpleNetworkConnection> clientTransport =
                new TcpClientTransport(tcpOptions ?? new TcpClientOptions());

            // We pass the null logger to avoid decoration of the resulting connection.
            ISimpleNetworkConnection clientConnection =
                clientTransport.CreateConnection(endpoint, NullLogger.Instance);

            return (TcpClientNetworkConnection)clientConnection;
        }

        private static async Task<TcpServerNetworkConnection> CreateServerConnection(
            IListener<ISimpleNetworkConnection> listener) =>
                (TcpServerNetworkConnection)await listener.AcceptAsync();

        private IListener<ISimpleNetworkConnection> CreateListener(
            string? host = null,
            TcpServerOptions? tcpOptions = null)
        {
            IServerTransport<ISimpleNetworkConnection> serverTransport =
                new TcpServerTransport(tcpOptions ?? new TcpServerOptions());
            host ??= _isIPv6 ? "[::1]" : "127.0.0.1";
            Endpoint endpoint = $"icerpc://{host}:0?tls=false";

            // We pass the null logger to avoid decoration of the listener.
            return serverTransport.Listen(endpoint, NullLogger.Instance);
        }
    }
}
