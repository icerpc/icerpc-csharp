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
            using IAcceptor acceptor = CreateAcceptor();
            using SingleStreamConnection outgoingConnection = CreateOutgoingConnection(new TcpOptions
            {
                SendBufferSize = size,
                ReceiveBufferSize = size
            });

            // The OS might allocate more space than the requested size.
            Assert.GreaterOrEqual(outgoingConnection.NetworkSocket!.SendBufferSize, size);
            Assert.GreaterOrEqual(outgoingConnection.NetworkSocket!.ReceiveBufferSize, size);

            // But ensure it doesn't allocate too much as well
            if (OperatingSystem.IsLinux())
            {
                // Linux allocates twice the size.
                Assert.LessOrEqual(outgoingConnection.NetworkSocket!.SendBufferSize, 2.5 * size);
                Assert.LessOrEqual(outgoingConnection.NetworkSocket!.ReceiveBufferSize, 2.5 * size);
            }
            else
            {
                // Windows typically allocates the requested size and macOS allocates a little more than the
                // requested size.
                Assert.LessOrEqual(outgoingConnection.NetworkSocket!.SendBufferSize, 1.5 * size);
                Assert.LessOrEqual(outgoingConnection.NetworkSocket!.ReceiveBufferSize, 1.5 * size);
            }
        }

        [Test]
        public void TcpOptions_Client_IsIPv6Only()
        {
            using IAcceptor acceptor = CreateAcceptor();
            using SingleStreamConnection outgoingConnection = CreateOutgoingConnection(new TcpOptions
            {
                IsIPv6Only = true
            });
            Assert.IsFalse(outgoingConnection.NetworkSocket!.DualMode);
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
                    using SingleStreamConnection outgoingConnection = CreateOutgoingConnection(new TcpOptions
                    {
                        LocalEndPoint = localEndPoint
                    });
                    Assert.AreEqual(localEndPoint, outgoingConnection.NetworkSocket!.LocalEndPoint);
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
            ValueTask<SingleStreamConnection> acceptTask = CreateIncomingConnectionAsync(acceptor);
            using SingleStreamConnection outgoingConnection = CreateOutgoingConnection();
            ValueTask<Endpoint> connectTask = outgoingConnection.ConnectAsync(
                ClientEndpoint,
                ClientAuthenticationOptions,
                default);
            using SingleStreamConnection incomingConnection = await acceptTask;

            // The OS might allocate more space than the requested size.
            Assert.GreaterOrEqual(incomingConnection.NetworkSocket!.SendBufferSize, size);
            Assert.GreaterOrEqual(incomingConnection.NetworkSocket!.ReceiveBufferSize, size);

            // But ensure it doesn't allocate too much as well
            if (OperatingSystem.IsMacOS())
            {
                // macOS Big Sur appears to have a low limit of a little more than 256KB for the receive buffer and
                // 64KB for the send buffer.
                Assert.LessOrEqual(incomingConnection.NetworkSocket!.SendBufferSize, 1.5 * Math.Max(size, 64 * 1024));
                Assert.LessOrEqual(incomingConnection.NetworkSocket!.ReceiveBufferSize, 1.5 * Math.Max(size, 256 * 1024));
            }
            else if (OperatingSystem.IsLinux())
            {
                // Linux allocates twice the size
                Assert.LessOrEqual(incomingConnection.NetworkSocket!.SendBufferSize, 2.5 * size);
                Assert.LessOrEqual(incomingConnection.NetworkSocket!.ReceiveBufferSize, 2.5 * size);
            }
            else
            {
                Assert.LessOrEqual(incomingConnection.NetworkSocket!.SendBufferSize, 1.5 * size);
                Assert.LessOrEqual(incomingConnection.NetworkSocket!.ReceiveBufferSize, 1.5 * size);
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
                IncomingConnectionOptions connectionOptions = IncomingConnectionOptions.Clone();
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

                using IAcceptor acceptor =
                    serverEndpoint.TransportDescriptor!.AcceptorFactory!(serverEndpoint, connectionOptions, Logger);

                ValueTask<SingleStreamConnection> acceptTask = CreateIncomingConnectionAsync(acceptor);

                // Create a client endpoints that uses the 127.0.0.1 IPv4-mapped address
                var data = new EndpointData(
                    ClientEndpoint.Transport,
                    "::FFFF:127.0.0.1",
                    ClientEndpoint.Port,
                    ClientEndpoint.Data.Options);

                var clientEndpoint = TcpEndpoint.CreateEndpoint(data, ClientEndpoint.Protocol);

                using SingleStreamConnection outgoingConnection = CreateOutgoingConnection(endpoint: clientEndpoint);

                ValueTask<Endpoint> connectTask =
                    outgoingConnection.ConnectAsync(clientEndpoint, null, default);

                if (ipv6Only)
                {
                    // This should fail, the server only accepts IPv6 connections
                    Assert.CatchAsync<ConnectFailedException>(async () => await connectTask);
                }
                else
                {
                    using SingleStreamConnection incomingConnection = await acceptTask;
                    ValueTask<Endpoint?> task = incomingConnection.AcceptAsync(serverEndpoint, null, default);

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
                ValueTask<SingleStreamConnection> acceptTask = CreateIncomingConnectionAsync(acceptor);
                var connections = new List<SingleStreamConnection>();
                while (true)
                {
                    using var source = new CancellationTokenSource(500);
                    SingleStreamConnection outgoingConnection = CreateOutgoingConnection();
                    try
                    {
                        await outgoingConnection.ConnectAsync(ClientEndpoint, ClientAuthenticationOptions, source.Token);
                        connections.Add(outgoingConnection);
                    }
                    catch (OperationCanceledException)
                    {
                        outgoingConnection.Dispose();
                        break;
                    }
                }

                // Tolerate a little more connections than the exact expected count (on Linux, it appears to accept one
                // more connection for instance).
                Assert.GreaterOrEqual(connections.Count, 19);
                Assert.LessOrEqual(connections.Count, 25);

                connections.ForEach(connection => connection.Dispose());
                acceptor.Dispose();
            }
        }

        private IAcceptor CreateAcceptorWithTcpOptions(TcpOptions options)
        {
            IncomingConnectionOptions connectionOptions = IncomingConnectionOptions.Clone();
            connectionOptions.TransportOptions = options;
            return ServerEndpoint.TransportDescriptor!.AcceptorFactory!(ServerEndpoint, connectionOptions, Logger);
        }

        private SingleStreamConnection CreateOutgoingConnection(TcpOptions? tcpOptions = null, Endpoint? endpoint = null)
        {
            OutgoingConnectionOptions options = OutgoingConnectionOptions.Clone();
            options.TransportOptions = tcpOptions ?? options.TransportOptions;
            endpoint ??= ClientEndpoint;

            return (endpoint.TransportDescriptor!.OutgoingConnectionFactory!(endpoint, options, Logger) as
                MultiStreamOverSingleStreamConnection)!.Underlying;
        }

        private static async ValueTask<SingleStreamConnection> CreateIncomingConnectionAsync(IAcceptor acceptor) =>
            ((await acceptor.AcceptAsync()) as MultiStreamOverSingleStreamConnection)!.Underlying;
    }
}
