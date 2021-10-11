// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using NUnit.Framework;
using System.Net;
using System.Net.Sockets;

namespace IceRpc.Tests.Internal
{
    [TestFixture(AddressFamily.InterNetwork)]
    [TestFixture(AddressFamily.InterNetwork)]
    [TestFixture(AddressFamily.InterNetworkV6)]
    [TestFixture(AddressFamily.InterNetworkV6)]
    [Timeout(5000)]
    public class TcpOptionsTests : NetworkSocketBaseTest
    {
        private bool _isIPv6;

        public TcpOptionsTests(AddressFamily addressFamily)
            : base("tcp", tls: false, addressFamily) =>
            _isIPv6 = addressFamily == AddressFamily.InterNetworkV6;

        [TestCase(16 * 1024)]
        [TestCase(64 * 1024)]
        [TestCase(256 * 1024)]
        [TestCase(384 * 1024)]
        public void TcpOptions_Client_BufferSize(int size)
        {
            using IListener listener = CreateListener();
            using NetworkSocket clientSocket = CreateClientSocket(
                new TcpOptions
                {
                    SendBufferSize = size,
                    ReceiveBufferSize = size
                });

            // The OS might allocate more space than the requested size.
            Assert.That(clientSocket.Socket!.SendBufferSize, Is.GreaterThanOrEqualTo(size));
            Assert.That(clientSocket.Socket!.ReceiveBufferSize, Is.GreaterThanOrEqualTo(size));

            // But ensure it doesn't allocate too much as well
            if (OperatingSystem.IsLinux())
            {
                // Linux allocates twice the size.
                Assert.That(clientSocket.Socket!.SendBufferSize, Is.LessThanOrEqualTo(2.5 * size));
                Assert.That(clientSocket.Socket!.ReceiveBufferSize, Is.LessThanOrEqualTo(2.5 * size));
            }
            else
            {
                // Windows typically allocates the requested size and macOS allocates a little more than the
                // requested size.
                Assert.That(clientSocket.Socket!.SendBufferSize, Is.LessThanOrEqualTo(1.5 * size));
                Assert.That(clientSocket.Socket!.ReceiveBufferSize, Is.LessThanOrEqualTo(1.5 * size));
            }
        }

        [Test]
        public void TcpOptions_Client_IsIPv6Only()
        {
            using IListener listener = CreateListener();
            using NetworkSocket clientSocket = CreateClientSocket(new TcpOptions { IsIPv6Only = true });
            Assert.That(clientSocket.Socket!.DualMode, Is.False);
        }

        [Test]
        public void TcpOptions_Client_LocalEndPoint()
        {
            int port = 45678;
            while (true)
            {
                try
                {
                    using IListener listener = CreateListener();
                    var localEndPoint = new IPEndPoint(_isIPv6 ? IPAddress.IPv6Loopback : IPAddress.Loopback, port++);
                    using NetworkSocket clientSocket = CreateClientSocket(
                        new TcpOptions { LocalEndPoint = localEndPoint });
                    Assert.AreEqual(localEndPoint, clientSocket.Socket!.LocalEndPoint);
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
            IListener listener = CreateListener(new TcpOptions
            {
                SendBufferSize = size,
                ReceiveBufferSize = size
            });
            ValueTask<NetworkSocket> acceptTask = CreateServerSocketAsync(listener);
            using NetworkSocket clientSocket = CreateClientSocket();
            ValueTask<Endpoint> connectTask = clientSocket.ConnectAsync(ClientEndpoint, default);
            using NetworkSocket serverSocket = await acceptTask;

            // The OS might allocate more space than the requested size.
            Assert.That(serverSocket.Socket!.SendBufferSize, Is.GreaterThanOrEqualTo(size));
            Assert.That(serverSocket.Socket!.ReceiveBufferSize, Is.GreaterThanOrEqualTo(size));

            // But ensure it doesn't allocate too much as well
            if (OperatingSystem.IsMacOS())
            {
                // macOS Big Sur appears to have a low limit of a little more than 256KB for the receive buffer and
                // 64KB for the send buffer.
                Assert.That(serverSocket.Socket!.SendBufferSize,
                            Is.LessThanOrEqualTo(1.5 * Math.Max(size, 64 * 1024)));
                Assert.That(serverSocket.Socket!.ReceiveBufferSize,
                            Is.LessThanOrEqualTo(1.5 * Math.Max(size, 256 * 1024)));
            }
            else if (OperatingSystem.IsLinux())
            {
                // Linux allocates twice the size
                Assert.That(serverSocket.Socket!.SendBufferSize, Is.LessThanOrEqualTo(2.5 * size));
                Assert.That(serverSocket.Socket!.ReceiveBufferSize, Is.LessThanOrEqualTo(2.5 * size));
            }
            else
            {
                Assert.That(serverSocket.Socket!.SendBufferSize, Is.LessThanOrEqualTo(1.5 * size));
                Assert.That(serverSocket.Socket!.ReceiveBufferSize, Is.LessThanOrEqualTo(1.5 * size));
            }
            listener.Dispose();
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task TcpOptions_Server_IsIPv6OnlyAsync(bool ipv6Only)
        {
            if (_isIPv6)
            {
                // Create a server endpoint for ::0 instead of loopback
                Endpoint serverEndpoint = ServerEndpoint with { Host = "::0" };

                using IListener listener = CreateListener(new TcpOptions() { IsIPv6Only = ipv6Only }, serverEndpoint);

                ValueTask<NetworkSocket> acceptTask = CreateServerSocketAsync(listener);

                // Create a client endpoints that uses the 127.0.0.1 IPv4-mapped address
                Endpoint clientEndpoint = ClientEndpoint with { Host = "::FFFF:127.0.0.1" };

                using NetworkSocket clientSocket = CreateClientSocket(endpoint: clientEndpoint);

                ValueTask<Endpoint> connectTask = clientSocket.ConnectAsync(clientEndpoint, default);

                if (ipv6Only)
                {
                    // This should fail, the server only accepts IPv6 connections
                    Assert.CatchAsync<ConnectFailedException>(async () => await connectTask);
                }
                else
                {
                    using NetworkSocket serverSocket = await acceptTask;
                    ValueTask<Endpoint> task = serverSocket.ConnectAsync(serverEndpoint, default);

                    // This should succeed, the server accepts IPv4 and IPv6 connections
                    Assert.DoesNotThrowAsync(async () => await connectTask);
                }
            }
        }

        [Test]
        public async Task TcpOptions_Server_ListenerBackLog()
        {
            IListener listener = CreateListener(new TcpOptions
            {
                ListenerBackLog = 18
            });
            ValueTask<NetworkSocket> acceptTask = CreateServerSocketAsync(listener);
            var connections = new List<NetworkSocket>();
            while (true)
            {
                using var source = new CancellationTokenSource(500);
                NetworkSocket clientSocket = CreateClientSocket();
                try
                {
                    await clientSocket.ConnectAsync(ClientEndpoint, source.Token);
                    connections.Add(clientSocket);
                }
                catch (OperationCanceledException)
                {
                    clientSocket.Dispose();
                    break;
                }
            }

            // Tolerate a little more connections than the exact expected count (on Linux, it appears to accept one
            // more connection for instance).
            Assert.That(connections.Count, Is.GreaterThanOrEqualTo(19));
            Assert.That(connections.Count, Is.LessThanOrEqualTo(25));

            connections.ForEach(connection => connection.Dispose());
            listener.Dispose();
        }

        private NetworkSocket CreateClientSocket(TcpOptions? options = null, Endpoint? endpoint = null) =>

            GetNetworkSocket(TestHelper.CreateClientTransport(
                endpoint ?? ClientEndpoint,
                options,
                logDecorator: false).CreateConnection(endpoint ?? ClientEndpoint));

        private static async ValueTask<NetworkSocket> CreateServerSocketAsync(IListener listener) =>
            GetNetworkSocket(await listener.AcceptAsync());
    }
}
