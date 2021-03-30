// Copyright (c) ZeroC, Inc. All rights reserved.

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
    public class SocketOptionsTests : SocketBaseTest
    {
        public SocketOptionsTests(Protocol protocol, AddressFamily addressFamily)
            : base(protocol, "tcp", NonSecure.Always, addressFamily)
        {
        }

        [Test]
        public void SocketOptions_Client_BufferSize()
        {
            using IAcceptor acceptor = CreateAcceptor();
            using SingleStreamSocket clientSocket = CreateClientSocket(new SocketOptions
            {
                SendBufferSize = 64 * 1024,
                ReceiveBufferSize = 64 * 1024
            });

            // The OS might allocate more space than the requested size.
            Assert.GreaterOrEqual(clientSocket.Socket!.SendBufferSize, 64 * 1024);
            Assert.GreaterOrEqual(clientSocket.Socket!.ReceiveBufferSize, 64 * 1024);

            // But ensure it doesn't allocate too much as well
            Assert.LessOrEqual(clientSocket.Socket!.SendBufferSize, 128 * 1024);
            Assert.LessOrEqual(clientSocket.Socket!.ReceiveBufferSize, 128 * 1024);
        }

        [Test]
        public void SocketOptions_Client_IsIPv6Only()
        {
            using IAcceptor acceptor = CreateAcceptor();
            using SingleStreamSocket clientSocket = CreateClientSocket(new SocketOptions
            {
                IsIPv6Only = true
            });
            if (IsIPv6)
            {
                Assert.IsFalse(clientSocket.Socket!.DualMode);
            }
            else
            {
                // Accessing DualMode for an IPv4 socket throws NotSupportedException
                Assert.Catch<NotSupportedException>(() => _ = clientSocket.Socket!.DualMode);
            }
        }

        [Test]
        public void SocketOptions_Client_LocalEndPoint()
        {
            int port = 45678;
            while (true)
            {
                try
                {
                    using IAcceptor acceptor = CreateAcceptor();
                    var localEndPoint = new IPEndPoint(IsIPv6 ? IPAddress.IPv6Loopback : IPAddress.Loopback, port++);
                    using SingleStreamSocket clientSocket = CreateClientSocket(new SocketOptions
                    {
                        LocalEndPoint = localEndPoint
                    });
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
        [TestCase(32 * 1024)]
        [TestCase(64 * 1024)]
        [TestCase(128 * 1024)]
        [TestCase(256 * 1024)]
        public async Task SocketOptions_Server_BufferSizeAsync(int size)
        {
            (Server server, IAcceptor acceptor) = CreateAcceptorWithSocketOptions(new SocketOptions
            {
                SendBufferSize = size,
                ReceiveBufferSize = size
            });
            ValueTask<SingleStreamSocket> acceptTask = CreateServerSocketAsync(acceptor);
            using SingleStreamSocket clientSocket = CreateClientSocket(new SocketOptions
            {
                SendBufferSize = size
            });
            ValueTask<SingleStreamSocket> connectTask = clientSocket.ConnectAsync(
                ClientEndpoint,
                ClientAuthenticationOptions,
                default);
            using SingleStreamSocket serverSocket = await acceptTask;

            // The OS might allocate more space than the requested size.
            Assert.GreaterOrEqual(serverSocket.Socket!.SendBufferSize, size);
            Assert.GreaterOrEqual(serverSocket.Socket!.ReceiveBufferSize, size);
            Console.Error.WriteLine($"{size} {serverSocket.Socket!.ReceiveBufferSize} {serverSocket.Socket!.SendBufferSize}");
            // But ensure it doesn't allocate too much as well
            Assert.LessOrEqual(serverSocket.Socket!.SendBufferSize, 512 * 1024);
            Assert.LessOrEqual(serverSocket.Socket!.ReceiveBufferSize, 512 * 1024);

            acceptor.Dispose();
            await server.DisposeAsync();
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task SocketOptions_Server_IsIPv6OnlyAsync(bool ipv6Only)
        {
            if (IsIPv6)
            {
                // Create a server endpoint for ::0 instead of loopback
                IncomingConnectionOptions connectionOptions = ServerConnectionOptions.Clone();
                connectionOptions.SocketOptions = new()
                {
                    IsIPv6Only = ipv6Only
                };
                await using var server = new Server(Communicator, new ServerOptions()
                {
                    ConnectionOptions = connectionOptions
                });

                var serverData = new EndpointData(
                    ServerEndpoint.Transport,
                    "::0",
                    ServerEndpoint.Port,
                    ServerEndpoint.Data.Options);

                TcpEndpoint serverEndpoint = TcpEndpoint.CreateEndpoint(serverData, ServerEndpoint.Protocol);

                using IAcceptor acceptor = serverEndpoint.Acceptor(server);

                ValueTask<SingleStreamSocket> acceptTask = CreateServerSocketAsync(acceptor);

                // Create a client endpoints that uses the 127.0.0.1 IPv4-mapped address
                var data = new EndpointData(
                    ClientEndpoint.Transport,
                    "::FFFF:127.0.0.1",
                    ClientEndpoint.Port,
                    ClientEndpoint.Data.Options);

                TcpEndpoint clientEndpoint = TcpEndpoint.CreateEndpoint(data, ClientEndpoint.Protocol);

                using SingleStreamSocket clientSocket = CreateClientSocket(endpoint: clientEndpoint);

                ValueTask<SingleStreamSocket> connectTask = clientSocket.ConnectAsync(clientEndpoint, null, default);

                if (ipv6Only)
                {
                    // This should fail, the server only accepts IPv6 connections
                    Assert.CatchAsync<ConnectFailedException>(async () => await connectTask);
                }
                else
                {
                    using SingleStreamSocket serverSocket = await acceptTask;
                    ValueTask<SingleStreamSocket> task = serverSocket.AcceptAsync(serverEndpoint, null, default);

                    // This should succeed, the server accepts IPv4 and IPv6 connections
                    Assert.DoesNotThrowAsync(async () => await connectTask);
                }

                acceptor.Dispose();
                await server.DisposeAsync();
            }
        }

        [Test]
        public async Task SocketOptions_Server_ListenerBackLog()
        {
            // This test can only work with TCP, ConnectAsync would block on other protocol initialization
            // (TLS handshake or WebSocket initialization).
            if (TransportName == "tcp" && !IsSecure)
            {
                (Server server, IAcceptor acceptor) = CreateAcceptorWithSocketOptions(new SocketOptions
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

                Assert.AreEqual(19, sockets.Count);

                sockets.ForEach(socket => socket.Dispose());
                acceptor.Dispose();
                await server.DisposeAsync();
            }
        }

        private (Server, IAcceptor) CreateAcceptorWithSocketOptions(SocketOptions options)
        {
            IncomingConnectionOptions connectionOptions = ServerConnectionOptions.Clone();
            connectionOptions.SocketOptions = options;
            var server = new Server(Communicator, new ServerOptions()
            {
                ConnectionOptions = connectionOptions
            });
            return (server, ServerEndpoint.Acceptor(server));
        }

        private SingleStreamSocket CreateClientSocket(SocketOptions? socketOptions = null, TcpEndpoint? endpoint = null)
        {
            var options = ClientConnectionOptions;
            socketOptions ??= options.SocketOptions!;
            endpoint ??= (TcpEndpoint)ClientEndpoint;
            EndPoint addr = new IPEndPoint(endpoint.Address, endpoint.Port);
            SingleStreamSocket socket = endpoint.CreateSocket(addr, socketOptions, Logger);
            MultiStreamOverSingleStreamSocket multiStreamSocket = ClientEndpoint.Protocol switch
            {
                Protocol.Ice1 => new Ice1NetworkSocket(ClientEndpoint, socket, options, Logger),
                _ => new SlicSocket(ClientEndpoint, socket, options, Logger)
            };
            Connection connection = endpoint.CreateConnection(multiStreamSocket, options, server: null);
            return (connection.Socket as MultiStreamOverSingleStreamSocket)!.Underlying;
        }
        private static async ValueTask<SingleStreamSocket> CreateServerSocketAsync(IAcceptor acceptor)
        {
            MultiStreamSocket multiStreamServerSocket = (await acceptor.AcceptAsync()).Socket;
            return (multiStreamServerSocket as MultiStreamOverSingleStreamSocket)!.Underlying;
        }
    }
}
