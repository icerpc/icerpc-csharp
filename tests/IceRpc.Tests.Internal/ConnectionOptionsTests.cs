// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.Internal
{
    [TestFixture(Protocol.Ice1, AddressFamily.InterNetwork)]
    [TestFixture(Protocol.Ice2, AddressFamily.InterNetwork)]
    [TestFixture(Protocol.Ice1, AddressFamily.InterNetworkV6)]
    [TestFixture(Protocol.Ice2, AddressFamily.InterNetworkV6)]
    [Timeout(5000)]
    public class TcpOptionsTests : ConnectionBaseTest
    {
        public TcpOptionsTests(Protocol protocol, AddressFamily addressFamily)
            : base(protocol, "tcp", tls: false, addressFamily)
        {
        }

        [TestCase(16 * 1024)]
        [TestCase(64 * 1024)]
        [TestCase(256 * 1024)]
        [TestCase(384 * 1024)]
        public void TcpOptions_Client_BufferSize(int size)
        {
            using IListener listener = CreateListener();
            using NetworkSocket clientConnection = CreateClientConnection(
                new TcpOptions
                {
                    SendBufferSize = size,
                    ReceiveBufferSize = size
                });

            // The OS might allocate more space than the requested size.
            Assert.That(clientConnection.Socket!.SendBufferSize, Is.GreaterThanOrEqualTo(size));
            Assert.That(clientConnection.Socket!.ReceiveBufferSize, Is.GreaterThanOrEqualTo(size));

            // But ensure it doesn't allocate too much as well
            if (OperatingSystem.IsLinux())
            {
                // Linux allocates twice the size.
                Assert.That(clientConnection.Socket!.SendBufferSize, Is.LessThanOrEqualTo(2.5 * size));
                Assert.That(clientConnection.Socket!.ReceiveBufferSize, Is.LessThanOrEqualTo(2.5 * size));
            }
            else
            {
                // Windows typically allocates the requested size and macOS allocates a little more than the
                // requested size.
                Assert.That(clientConnection.Socket!.SendBufferSize, Is.LessThanOrEqualTo(1.5 * size));
                Assert.That(clientConnection.Socket!.ReceiveBufferSize, Is.LessThanOrEqualTo(1.5 * size));
            }
        }

        [Test]
        public void TcpOptions_Client_IsIPv6Only()
        {
            using IListener listener = CreateListener();
            using NetworkSocket clientConnection = CreateClientConnection(
                new TcpOptions { IsIPv6Only = true });
            Assert.That(clientConnection.Socket!.DualMode, Is.False);
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
                    var localEndPoint = new IPEndPoint(IsIPv6 ? IPAddress.IPv6Loopback : IPAddress.Loopback, port++);
                    using NetworkSocket clientConnection = CreateClientConnection(
                        new TcpOptions { LocalEndPoint = localEndPoint });
                    Assert.AreEqual(localEndPoint, clientConnection.Socket!.LocalEndPoint);
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
            ValueTask<NetworkSocket> acceptTask = CreateServerConnectionAsync(listener);
            using NetworkSocket clientConnection = CreateClientConnection();
            ValueTask<Endpoint> connectTask = clientConnection.ConnectAsync(
                ClientEndpoint,
                ClientAuthenticationOptions,
                default);
            using NetworkSocket serverConnection = await acceptTask;

            // The OS might allocate more space than the requested size.
            Assert.That(serverConnection.Socket!.SendBufferSize, Is.GreaterThanOrEqualTo(size));
            Assert.That(serverConnection.Socket!.ReceiveBufferSize, Is.GreaterThanOrEqualTo(size));

            // But ensure it doesn't allocate too much as well
            if (OperatingSystem.IsMacOS())
            {
                // macOS Big Sur appears to have a low limit of a little more than 256KB for the receive buffer and
                // 64KB for the send buffer.
                Assert.That(serverConnection.Socket!.SendBufferSize,
                            Is.LessThanOrEqualTo(1.5 * Math.Max(size, 64 * 1024)));
                Assert.That(serverConnection.Socket!.ReceiveBufferSize,
                            Is.LessThanOrEqualTo(1.5 * Math.Max(size, 256 * 1024)));
            }
            else if (OperatingSystem.IsLinux())
            {
                // Linux allocates twice the size
                Assert.That(serverConnection.Socket!.SendBufferSize, Is.LessThanOrEqualTo(2.5 * size));
                Assert.That(serverConnection.Socket!.ReceiveBufferSize, Is.LessThanOrEqualTo(2.5 * size));
            }
            else
            {
                Assert.That(serverConnection.Socket!.SendBufferSize, Is.LessThanOrEqualTo(1.5 * size));
                Assert.That(serverConnection.Socket!.ReceiveBufferSize, Is.LessThanOrEqualTo(1.5 * size));
            }
            listener.Dispose();
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task TcpOptions_Server_IsIPv6OnlyAsync(bool ipv6Only)
        {
            if (IsIPv6)
            {
                // Create a server endpoint for ::0 instead of loopback
                ServerConnectionOptions connectionOptions = ServerConnectionOptions.Clone();

                Endpoint serverEndpoint = ServerEndpoint with { Host = "::0" };

                using IListener listener = CreateListener(
                    new TcpOptions() { IsIPv6Only = ipv6Only },
                    serverEndpoint);

                ValueTask<NetworkSocket> acceptTask = CreateServerConnectionAsync(listener);

                // Create a client endpoints that uses the 127.0.0.1 IPv4-mapped address
                Endpoint clientEndpoint = ClientEndpoint with { Host = "::FFFF:127.0.0.1" };

                using NetworkSocket clientConnection = CreateClientConnection(endpoint: clientEndpoint);

                ValueTask<Endpoint> connectTask = clientConnection.ConnectAsync(clientEndpoint, null, default);

                if (ipv6Only)
                {
                    // This should fail, the server only accepts IPv6 connections
                    Assert.CatchAsync<ConnectFailedException>(async () => await connectTask);
                }
                else
                {
                    using NetworkSocket serverConnection = await acceptTask;
                    ValueTask<Endpoint?> task = serverConnection.AcceptAsync(serverEndpoint, null, default);

                    // This should succeed, the server accepts IPv4 and IPv6 connections
                    Assert.DoesNotThrowAsync(async () => await connectTask);
                }
            }
        }

        [Test]
        public async Task TcpOptions_Server_ListenerBackLog()
        {
            // This test can only work with TCP, ConnectAsync would block on other protocol initialization
            // (TLS handshake or WebSocket initialization).
            if (TransportName == "tcp" && !IsSecure)
            {
                IListener listener = CreateListener(new TcpOptions
                {
                    ListenerBackLog = 18
                });
                ValueTask<NetworkSocket> acceptTask = CreateServerConnectionAsync(listener);
                var connections = new List<NetworkSocket>();
                while (true)
                {
                    using var source = new CancellationTokenSource(500);
                    NetworkSocket clientConnection = CreateClientConnection();
                    try
                    {
                        await clientConnection.ConnectAsync(ClientEndpoint, ClientAuthenticationOptions, source.Token);
                        connections.Add(clientConnection);
                    }
                    catch (OperationCanceledException)
                    {
                        clientConnection.Dispose();
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
        }

        private NetworkSocket CreateClientConnection(TcpOptions? options = null, Endpoint? endpoint = null)
        {
            endpoint ??= ClientEndpoint;

            IClientTransport transport = options == null ? Connection.DefaultClientTransport : new TcpClientTransport(options);

            return (transport.CreateConnection(
                endpoint,
                ClientConnectionOptions.Clone(),
                LogAttributeLoggerFactory.Instance) as NetworkSocketConnection)!.NetworkSocket;
        }

        private static async ValueTask<NetworkSocket> CreateServerConnectionAsync(IListener listener) =>
            ((await listener.AcceptAsync()) as NetworkSocketConnection)!.NetworkSocket;
    }
}
