// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
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

            SingleStreamSocket socket = await serverSocket.AcceptAsync(
                ServerEndpoint,
                ServerAuthenticationOptions,
                default);
            await connectTask;

            // The SslSocket is returned if a secure connection is requested.
            Assert.IsTrue(IsSecure ? socket != serverSocket : socket == serverSocket);
        }

        // We eventually retry this test if it fails. The AcceptAsync can indeed not always fail if for
        // example the server SSL handshake completes before the RST is received.
        [Test]
        public async Task AcceptSingleStreamSocket_AcceptAsync_ConnectionLostException()
        {
            using IAcceptor acceptor = CreateAcceptor();
            ValueTask<SingleStreamSocket> acceptTask = CreateServerSocketAsync(acceptor);

            SingleStreamSocket clientSocket = CreateClientSocket();

            // We don't use clientSocket.ConnectAsync() here as this would start the TLS handshake for secure
            // connections and AcceptAsync would sometime succeed.
            IPAddress[] addresses = await Dns.GetHostAddressesAsync(ClientEndpoint.Host).ConfigureAwait(false);
            var endpoint = new IPEndPoint(addresses[0], ClientEndpoint.Port);
            await clientSocket.Socket!.ConnectAsync(endpoint).ConfigureAwait(false);

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

        [Test]
        public async Task AcceptSingleStreamSocket_AcceptAsync_OperationCanceledException()
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
            SingleStreamSocket socket = endpoint.CreateSocket(addr, options.SocketOptions!, Logger);
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
