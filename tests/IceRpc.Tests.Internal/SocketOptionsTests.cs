// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
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
    public class TcpOptionsTests : SocketBaseTest
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
            using IAcceptor acceptor = CreateAcceptor();
            using SingleStreamSocket clientSocket = CreateClientSocket(new TcpOptions
            {
                SendBufferSize = size,
                ReceiveBufferSize = size
            });

            // The OS might allocate more space than the requested size.
            Assert.GreaterOrEqual(clientSocket.NetworkSocket!.SendBufferSize, size);
            Assert.GreaterOrEqual(clientSocket.NetworkSocket!.ReceiveBufferSize, size);

            // But ensure it doesn't allocate too much as well
            if (OperatingSystem.IsLinux())
            {
                // Linux allocates twice the size.
                Assert.LessOrEqual(clientSocket.NetworkSocket!.SendBufferSize, 2.5 * size);
                Assert.LessOrEqual(clientSocket.NetworkSocket!.ReceiveBufferSize, 2.5 * size);
            }
            else
            {
                // Windows typically allocates the requested size and macOS allocates a little more than the
                // requested size.
                Assert.LessOrEqual(clientSocket.NetworkSocket!.SendBufferSize, 1.5 * size);
                Assert.LessOrEqual(clientSocket.NetworkSocket!.ReceiveBufferSize, 1.5 * size);
            }
        }

        [Test]
        public void TcpOptions_Client_IsIPv6Only()
        {
            using IAcceptor acceptor = CreateAcceptor();
            using SingleStreamSocket clientSocket = CreateClientSocket(new TcpOptions
            {
                IsIPv6Only = true
            });
            if (IsIPv6)
            {
                Assert.IsFalse(clientSocket.NetworkSocket!.DualMode);
            }
            else
            {
                // Accessing DualMode for an IPv4 socket throws NotSupportedException
                Assert.Catch<NotSupportedException>(() => _ = clientSocket.NetworkSocket!.DualMode);
            }
        }

        [Test]
        public void TcpOptions_Client_LocalEndPoint()
        {
            int port = 45678;
            while (true)
            {
                try
                {
                    using IAcceptor acceptor = CreateAcceptor();
                    var localEndPoint = new IPEndPoint(IsIPv6 ? IPAddress.IPv6Loopback : IPAddress.Loopback, port++);
                    using SingleStreamSocket clientSocket = CreateClientSocket(new TcpOptions
                    {
                        LocalEndPoint = localEndPoint
                    });
                    Assert.AreEqual(localEndPoint, clientSocket.NetworkSocket!.LocalEndPoint);
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
            IAcceptor acceptor = CreateAcceptorWithTcpOptions(new TcpOptions
            {
                SendBufferSize = size,
                ReceiveBufferSize = size
            });
            ValueTask<SingleStreamSocket> acceptTask = CreateServerSocketAsync(acceptor);
            using SingleStreamSocket clientSocket = CreateClientSocket();
            ValueTask<(SingleStreamSocket, Endpoint)> connectTask = clientSocket.ConnectAsync(
                ClientEndpoint,
                ClientAuthenticationOptions,
                default);
            using SingleStreamSocket serverSocket = await acceptTask;

            // The OS might allocate more space than the requested size.
            Assert.GreaterOrEqual(serverSocket.NetworkSocket!.SendBufferSize, size);
            Assert.GreaterOrEqual(serverSocket.NetworkSocket!.ReceiveBufferSize, size);

            // But ensure it doesn't allocate too much as well
            if (OperatingSystem.IsMacOS())
            {
                // macOS Big Sur appears to have a low limit of a little more than 256KB for the receive buffer and
                // 64KB for the send buffer.
                Assert.LessOrEqual(serverSocket.NetworkSocket!.SendBufferSize, 1.5 * Math.Max(size, 64 * 1024));
                Assert.LessOrEqual(serverSocket.NetworkSocket!.ReceiveBufferSize, 1.5 * Math.Max(size, 256 * 1024));
            }
            else if (OperatingSystem.IsLinux())
            {
                // Linux allocates twice the size
                Assert.LessOrEqual(serverSocket.NetworkSocket!.SendBufferSize, 2.5 * size);
                Assert.LessOrEqual(serverSocket.NetworkSocket!.ReceiveBufferSize, 2.5 * size);
            }
            else
            {
                Assert.LessOrEqual(serverSocket.NetworkSocket!.SendBufferSize, 1.5 * size);
                Assert.LessOrEqual(serverSocket.NetworkSocket!.ReceiveBufferSize, 1.5 * size);
            }
            acceptor.Dispose();
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task TcpOptions_Server_IsIPv6OnlyAsync(bool ipv6Only)
        {
            if (IsIPv6)
            {
                // Create a server endpoint for ::0 instead of loopback
                IncomingConnectionOptions connectionOptions = ServerConnectionOptions.Clone();
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

                using IAcceptor acceptor = serverEndpoint.CreateAcceptor(connectionOptions, Logger);

                ValueTask<SingleStreamSocket> acceptTask = CreateServerSocketAsync(acceptor);

                // Create a client endpoints that uses the 127.0.0.1 IPv4-mapped address
                var data = new EndpointData(
                    ClientEndpoint.Transport,
                    "::FFFF:127.0.0.1",
                    ClientEndpoint.Port,
                    ClientEndpoint.Data.Options);

                var clientEndpoint = TcpEndpoint.CreateEndpoint(data, ClientEndpoint.Protocol);

                using SingleStreamSocket clientSocket = CreateClientSocket(endpoint: clientEndpoint);

                ValueTask<(SingleStreamSocket, Endpoint)> connectTask =
                    clientSocket.ConnectAsync(clientEndpoint, null, default);

                if (ipv6Only)
                {
                    // This should fail, the server only accepts IPv6 connections
                    Assert.CatchAsync<ConnectFailedException>(async () => await connectTask);
                }
                else
                {
                    using SingleStreamSocket serverSocket = await acceptTask;
                    ValueTask<(SingleStreamSocket, Endpoint?)> task =
                        serverSocket.AcceptAsync(serverEndpoint, null, default);

                    // This should succeed, the server accepts IPv4 and IPv6 connections
                    Assert.DoesNotThrowAsync(async () => await connectTask);
                }

                acceptor.Dispose();
            }
        }

        [Test]
        public async Task TcpOptions_Server_ListenerBackLog()
        {
            // This test can only work with TCP, ConnectAsync would block on other protocol initialization
            // (TLS handshake or WebSocket initialization).
            if (TransportName == "tcp" && !IsSecure)
            {
                IAcceptor acceptor = CreateAcceptorWithTcpOptions(new TcpOptions
                {
                    ListenerBackLog = 18
                });
                ValueTask<SingleStreamSocket> acceptTask = CreateServerSocketAsync(acceptor);
                var sockets = new List<SingleStreamSocket>();
                while (true)
                {
                    using var source = new CancellationTokenSource(500);
                    SingleStreamSocket clientSocket = CreateClientSocket();
                    try
                    {
                        await clientSocket.ConnectAsync(ClientEndpoint, ClientAuthenticationOptions, source.Token);
                        sockets.Add(clientSocket);
                    }
                    catch (OperationCanceledException)
                    {
                        clientSocket.Dispose();
                        break;
                    }
                }

                // Tolerate a little more sockets than the exact expected count (on Linux, it appears to accept one
                // more socket for instance).
                Assert.GreaterOrEqual(sockets.Count, 19);
                Assert.LessOrEqual(sockets.Count, 25);

                sockets.ForEach(socket => socket.Dispose());
                acceptor.Dispose();
            }
        }

        private IAcceptor CreateAcceptorWithTcpOptions(TcpOptions options)
        {
            IncomingConnectionOptions connectionOptions = ServerConnectionOptions.Clone();
            connectionOptions.TransportOptions = options;
            return ServerEndpoint.CreateAcceptor(connectionOptions, Logger);
        }

        private SingleStreamSocket CreateClientSocket(TcpOptions? tcpOptions = null, TcpEndpoint? endpoint = null)
        {
            OutgoingConnectionOptions options = ClientConnectionOptions.Clone();
            options.TransportOptions = tcpOptions ?? options.TransportOptions;
            return ((endpoint ?? ClientEndpoint).CreateClientSocket(
                   options,
                   Logger) as MultiStreamOverSingleStreamSocket)!.Underlying;
        }

        private static async ValueTask<SingleStreamSocket> CreateServerSocketAsync(IAcceptor acceptor) =>
            ((await acceptor.AcceptAsync()) as MultiStreamOverSingleStreamSocket)!.Underlying;
    }
}
