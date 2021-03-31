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
    // Testing the Ice1 and Ice2 protocol here is useful because of the handling of secure vs non-secure
    // incoming connection which is different (with Ice2, the acceptor peeks a byte on the socket to
    // figure out if the outgoing connection is a secure or non-secure connection).
    [TestFixture(Protocol.Ice2, "tcp", NonSecure.Always, AddressFamily.InterNetwork)]
    [TestFixture(Protocol.Ice2, "tcp", NonSecure.Never, AddressFamily.InterNetwork)]
    [TestFixture(Protocol.Ice2, "ws", NonSecure.Always, AddressFamily.InterNetwork)]
    [TestFixture(Protocol.Ice2, "ws", NonSecure.Never, AddressFamily.InterNetwork)]
    [TestFixture(Protocol.Ice1, "tcp", NonSecure.Always, AddressFamily.InterNetwork)]
    [TestFixture(Protocol.Ice1, "ssl", NonSecure.Never, AddressFamily.InterNetwork)]
    [TestFixture(Protocol.Ice1, "tcp", NonSecure.Always, AddressFamily.InterNetworkV6)]
    [TestFixture(Protocol.Ice2, "tcp", NonSecure.Always, AddressFamily.InterNetworkV6)]
    [Timeout(5000)]
    public class AcceptSingleStreamSocketTests : SocketBaseTest
    {
        public AcceptSingleStreamSocketTests(
            Protocol protocol,
            string transport,
            NonSecure nonSecure,
            AddressFamily addressFamily)
            : base(protocol, transport, nonSecure, addressFamily)
        {
        }

        [Test]
        public async Task AcceptSingleStreamSocket_Acceptor_AcceptAsync()
        {
            using IAcceptor acceptor = CreateAcceptor();
            ValueTask<SingleStreamSocket> acceptTask = CreateServerSocketAsync(acceptor);

            using SingleStreamSocket clientSocket = CreateClientSocket();
            ValueTask<SingleStreamSocket> connectTask = clientSocket.ConnectAsync(
                ClientEndpoint,
                ClientAuthenticationOptions,
                default);
            using SingleStreamSocket serverSocket = await acceptTask;
        }

        [Test]
        public void AcceptSingleStreamSocket_Acceptor_Constructor_TransportException()
        {
            using IAcceptor acceptor = CreateAcceptor();
            Assert.Throws<TransportException>(() => CreateAcceptor());
        }

        [Test]
        public async Task AcceptSingleStreamSocket_AcceptAsync()
        {
            using IAcceptor acceptor = CreateAcceptor();
            ValueTask<SingleStreamSocket> acceptTask = CreateServerSocketAsync(acceptor);

            using SingleStreamSocket clientSocket = CreateClientSocket();
            ValueTask<SingleStreamSocket> connectTask = clientSocket.ConnectAsync(
                ClientEndpoint,
                ClientAuthenticationOptions,
                default);

            using SingleStreamSocket serverSocket = await acceptTask;

            ValueTask<SingleStreamSocket> acceptTask2 = serverSocket.AcceptAsync(
                ServerEndpoint,
                ServerAuthenticationOptions,
                default);

            await connectTask;

            if (ClientEndpoint.Protocol == Protocol.Ice2 && TransportName == "tcp")
            {
                await clientSocket.SendAsync(new List<ArraySegment<byte>> { new byte[1] }, default);
            }

            SingleStreamSocket socket = await acceptTask2;

            // The SslSocket is returned if a secure connection is requested.
            if (IsSecure && TransportName != "ws")
            {
                Assert.IsInstanceOf<SslSocket>(socket);
            }
            else
            {
                Assert.IsNotInstanceOf<SslSocket>(socket);
            }
        }

        // We eventually retry this test if it fails. The AcceptAsync can indeed not always fail if for
        // example the server SSL handshake completes before the RST is received.
        [Test]
        public async Task AcceptSingleStreamSocket_AcceptAsync_ConnectionLostExceptionAsync()
        {
            using IAcceptor acceptor = CreateAcceptor();
            ValueTask<SingleStreamSocket> acceptTask = CreateServerSocketAsync(acceptor);

            SingleStreamSocket clientSocket = CreateClientSocket();

            // We don't use clientSocket.ConnectAsync() here as this would start the TLS handshake for secure
            // connections and AcceptAsync would sometime succeed.
            await clientSocket.Socket!.ConnectAsync(
                new DnsEndPoint(ClientEndpoint.Host, ClientEndpoint.Port)).ConfigureAwait(false);

            using SingleStreamSocket serverSocket = await acceptTask;

            clientSocket.Dispose();

            AsyncTestDelegate testDelegate;
            if (!IsSecure && ClientEndpoint.Protocol == Protocol.Ice1 && TransportName == "tcp")
            {
                // AcceptAsync is a no-op for Ice1 non-secure TCP connections so it won't throw.
                await serverSocket.AcceptAsync(
                    ServerEndpoint,
                    ServerAuthenticationOptions,
                    default);
                testDelegate = async () => await serverSocket.ReceiveAsync(new byte[1], default);
            }
            else
            {
                testDelegate = async () => await serverSocket.AcceptAsync(
                    ServerEndpoint,
                    ServerAuthenticationOptions,
                    default);
            }
            Assert.ThrowsAsync<ConnectionLostException>(testDelegate);
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public void AcceptSingleStreamSocket_Acceptor_AddressReuse(bool wildcard1, bool wildcard2)
        {
            IAcceptor acceptor;
            if (wildcard1)
            {
                var serverData = new EndpointData(
                    ServerEndpoint.Transport,
                    "::0",
                    ServerEndpoint.Port,
                    ServerEndpoint.Data.Options);
                var serverEndpoint = TcpEndpoint.CreateEndpoint(serverData, ServerEndpoint.Protocol);
                acceptor = serverEndpoint.Acceptor(Server);
            }
            else
            {
                acceptor = CreateAcceptor();
            }

            if (wildcard2)
            {
                var serverData = new EndpointData(
                    ServerEndpoint.Transport,
                    "::0",
                    ServerEndpoint.Port,
                    ServerEndpoint.Data.Options);
                var serverEndpoint = TcpEndpoint.CreateEndpoint(serverData, ServerEndpoint.Protocol);

                if (OperatingSystem.IsMacOS())
                {
                    // On macOS, it's still possible to bind to a specific address even if a socket is bound
                    // to the wildcard address.
                    Assert.DoesNotThrow(() => serverEndpoint.Acceptor(Server).Dispose());
                }
                else
                {
                    Assert.Catch<TransportException>(() => serverEndpoint.Acceptor(Server));
                }
            }
            else
            {
                if (wildcard1 && OperatingSystem.IsMacOS())
                {
                    // On macOS, it's still possible to bind to a specific address even if a socket is bound
                    // to the wildcard address.
                    Assert.DoesNotThrow(() => CreateAcceptor().Dispose());
                }
                else
                {
                    Assert.Catch<TransportException>(() => CreateAcceptor());
                }
            }

            acceptor.Dispose();
        }

        [Test]
        public async Task AcceptSingleStreamSocket_AcceptAsync_OperationCanceledExceptionAsync()
        {
            using IAcceptor acceptor = CreateAcceptor();

            using SingleStreamSocket clientSocket = CreateClientSocket();
            ValueTask<SingleStreamSocket> connectTask = clientSocket.ConnectAsync(
                ClientEndpoint,
                ClientAuthenticationOptions,
                default);

            using SingleStreamSocket serverSocket = await CreateServerSocketAsync(acceptor);

            using var source = new CancellationTokenSource();
            source.Cancel();
            ValueTask<SingleStreamSocket> acceptTask = serverSocket.AcceptAsync(
                    ServerEndpoint,
                    ServerAuthenticationOptions,
                    source.Token);

            if (!IsSecure && ClientEndpoint.Protocol == Protocol.Ice1 && TransportName == "tcp")
            {
                // AcceptAsync is a no-op for Ice1 non-secure TCP connections so it won't throw.
                await acceptTask;
            }
            else
            {
                Assert.CatchAsync<OperationCanceledException>(async () => await acceptTask);
            }
        }

        private SingleStreamSocket CreateClientSocket()
        {
            var endpoint = (TcpEndpoint)ClientEndpoint;
            OutgoingConnectionOptions options = ClientConnectionOptions;
            EndPoint addr = new IPEndPoint(endpoint.Address, endpoint.Port);
            TcpOptions tcpOptions = options.TransportOptions as TcpOptions ?? TcpOptions.Default;
            SingleStreamSocket socket = endpoint.CreateSocket(addr, tcpOptions, Logger);
            MultiStreamOverSingleStreamSocket multiStreamSocket = ClientEndpoint.Protocol switch
            {
                Protocol.Ice1 => new Ice1NetworkSocket(ClientEndpoint, socket, options),
                _ => new SlicSocket(ClientEndpoint, socket, options)
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
