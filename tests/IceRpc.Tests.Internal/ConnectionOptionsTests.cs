// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using IceRpc.Transports.Internal;
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
            using NetworkSocket clientConnection = CreateClientConnection(new TcpOptions
            {
                SendBufferSize = size,
                ReceiveBufferSize = size
            });

            // The OS might allocate more space than the requested size.
            Assert.GreaterOrEqual(clientConnection.Socket!.SendBufferSize, size);
            Assert.GreaterOrEqual(clientConnection.Socket!.ReceiveBufferSize, size);

            // But ensure it doesn't allocate too much as well
            if (OperatingSystem.IsLinux())
            {
                // Linux allocates twice the size.
                Assert.LessOrEqual(clientConnection.Socket!.SendBufferSize, 2.5 * size);
                Assert.LessOrEqual(clientConnection.Socket!.ReceiveBufferSize, 2.5 * size);
            }
            else
            {
                // Windows typically allocates the requested size and macOS allocates a little more than the
                // requested size.
                Assert.LessOrEqual(clientConnection.Socket!.SendBufferSize, 1.5 * size);
                Assert.LessOrEqual(clientConnection.Socket!.ReceiveBufferSize, 1.5 * size);
            }
        }

        [Test]
        public void TcpOptions_Client_IsIPv6Only()
        {
            using IListener listener = CreateListener();
            using NetworkSocket clientConnection = CreateClientConnection(new TcpOptions
            {
                IsIPv6Only = true
            });
            Assert.IsFalse(clientConnection.Socket!.DualMode);
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
                    using NetworkSocket clientConnection = CreateClientConnection(new TcpOptions
                    {
                        LocalEndPoint = localEndPoint
                    });
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
            IListener listener = CreateListenerWithTcpOptions(new TcpOptions
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
            Assert.GreaterOrEqual(serverConnection.Socket!.SendBufferSize, size);
            Assert.GreaterOrEqual(serverConnection.Socket!.ReceiveBufferSize, size);

            // But ensure it doesn't allocate too much as well
            if (OperatingSystem.IsMacOS())
            {
                // macOS Big Sur appears to have a low limit of a little more than 256KB for the receive buffer and
                // 64KB for the send buffer.
                Assert.LessOrEqual(serverConnection.Socket!.SendBufferSize, 1.5 * Math.Max(size, 64 * 1024));
                Assert.LessOrEqual(serverConnection.Socket!.ReceiveBufferSize, 1.5 * Math.Max(size, 256 * 1024));
            }
            else if (OperatingSystem.IsLinux())
            {
                // Linux allocates twice the size
                Assert.LessOrEqual(serverConnection.Socket!.SendBufferSize, 2.5 * size);
                Assert.LessOrEqual(serverConnection.Socket!.ReceiveBufferSize, 2.5 * size);
            }
            else
            {
                Assert.LessOrEqual(serverConnection.Socket!.SendBufferSize, 1.5 * size);
                Assert.LessOrEqual(serverConnection.Socket!.ReceiveBufferSize, 1.5 * size);
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
                connectionOptions.TransportOptions = new TcpOptions()
                {
                    IsIPv6Only = ipv6Only
                };

                var serverData = new EndpointData(
                    ServerEndpoint.Transport,
                    "::0",
                    ServerEndpoint.Port,
                    ServerEndpoint.Data.Options);

                var serverEndpoint = TcpEndpoint.CreateEndpoint(serverData, ServerEndpoint.Protocol);

                using IListener listener =
                    serverEndpoint.TransportDescriptor!.ListenerFactory!(serverEndpoint, connectionOptions, Logger);

                ValueTask<NetworkSocket> acceptTask = CreateServerConnectionAsync(listener);

                // Create a client endpoints that uses the 127.0.0.1 IPv4-mapped address
                var data = new EndpointData(
                    ClientEndpoint.Transport,
                    "::FFFF:127.0.0.1",
                    ClientEndpoint.Port,
                    ClientEndpoint.Data.Options);

                var clientEndpoint = TcpEndpoint.CreateEndpoint(data, ClientEndpoint.Protocol);

                using NetworkSocket clientConnection = CreateClientConnection(endpoint: clientEndpoint);

                ValueTask<Endpoint> connectTask =
                    clientConnection.ConnectAsync(clientEndpoint, null, default);

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

                listener.Dispose();
            }
        }

        [Test]
        public async Task TcpOptions_Server_ListenerBackLog()
        {
            // This test can only work with TCP, ConnectAsync would block on other protocol initialization
            // (TLS handshake or WebSocket initialization).
            if (TransportName == "tcp" && !IsSecure)
            {
                IListener listener = CreateListenerWithTcpOptions(new TcpOptions
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
                Assert.GreaterOrEqual(connections.Count, 19);
                Assert.LessOrEqual(connections.Count, 25);

                connections.ForEach(connection => connection.Dispose());
                listener.Dispose();
            }
        }

        private IListener CreateListenerWithTcpOptions(TcpOptions options)
        {
            ServerConnectionOptions connectionOptions = ServerConnectionOptions.Clone();
            connectionOptions.TransportOptions = options;
            return ServerEndpoint.TransportDescriptor!.ListenerFactory!(ServerEndpoint, connectionOptions, Logger);
        }

        private NetworkSocket CreateClientConnection(TcpOptions? tcpOptions = null, Endpoint? endpoint = null)
        {
            ClientConnectionOptions options = ClientConnectionOptions.Clone();
            options.TransportOptions = tcpOptions ?? options.TransportOptions;
            endpoint ??= ClientEndpoint;

            return (endpoint.TransportDescriptor!.Connector!(endpoint, options, Logger) as
                NetworkSocketConnection)!.Underlying;
        }

        private static async ValueTask<NetworkSocket> CreateServerConnectionAsync(IListener listener) =>
            ((await listener.AcceptAsync()) as NetworkSocketConnection)!.Underlying;
    }
}
